using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MazeRace
{
    public enum Direction { Right, Up, Left, Down }
    public enum WallSide { Top, Left, Bottom, Right }

    public struct HitWall
    {
        public int X, Y;
        public WallSide Side;
        public HitWall(int x, int y, WallSide side) { X = x; Y = y; Side = side; }
    }

    public class Coin
    {
        public Point Position;
        public bool Collected;
    }

    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }
    }

    public class GameForm : Form
    {
        private string playerName;
        private MazeData maze;
        private Point playerPos;
        private Direction playerDir = Direction.Right;
        private bool gameStarted = false;
        private bool finished = false;
        private Stopwatch timer = new Stopwatch();
        private Point spawnPoint;

        private const double halfAngle = 15.0;
        private bool[,] explored;

        private Label lblTimer;
        private Label lblScore;
        private Label lblStatus;
        private BufferedPanel gamePanel;

        private int baseCellSize;
        private int baseCellSizeNormal;
        private int CurrentCellSize => pendingTeleport.HasValue ? Math.Max(baseCellSize / 3, 20) : baseCellSize;

        private PointF[] currentLightPolygon;
        private HashSet<HitWall> currentHitWalls;

        private int difficulty;

        private List<Point> teleporters = new List<Point>();
        private Point? pendingTeleport = null;
        private HashSet<Point> teleporterTargets = new HashSet<Point>();

        private List<Coin> coins = new List<Coin>();
        private int score = 0;
        private int coinsCollected = 0;
        private const int coinValue = 10;
        private Timer statusTimer = new Timer();

        // 金币生成范围：终点路径及附近格子
        private HashSet<Point> nearPathCells = new HashSet<Point>();

        public GameForm(string name, int difficulty)
        {
            this.difficulty = difficulty;
            playerName = name;
            Text = "迷宫竞速 - " + playerName;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += OnKeyDown;

            GenerateMaze();

            int panelSize = 1200;
            int maxCellsX = Math.Max(maze.Width, maze.Height) + 4;
            baseCellSizeNormal = Math.Max(20, panelSize / maxCellsX) * 3;
            baseCellSize = baseCellSizeNormal;
            ClientSize = new Size(1600, 1300);

            Panel leftPanel = new Panel
            {
                Location = new Point(20, 20),
                Size = new Size(300, 1200),
                BorderStyle = BorderStyle.FixedSingle
            };
            lblTimer = new Label
            {
                Text = "按方向键开始",
                Location = new Point(20, 20),
                Size = new Size(260, 40),
                Font = new Font("微软雅黑", 13, FontStyle.Bold)
            };
            leftPanel.Controls.Add(lblTimer);

            lblScore = new Label
            {
                Text = "积分: 0  金币: 0",
                Location = new Point(20, 70),
                Size = new Size(260, 40),
                Font = new Font("微软雅黑", 12, FontStyle.Bold),
                ForeColor = Color.Goldenrod
            };
            leftPanel.Controls.Add(lblScore);

            Label lblHelp = new Label
            {
                Text = "用最快的速度\n走到迷宫最右侧的出口吧！\n蓝色标记是出生点\n紫色标记是传送阵\n\nTips:金币只会出现在通路附近！\n\n难度：" + difficulty,
                Location = new Point(20, 120),
                Size = new Size(260, 180),
                Font = new Font("微软雅黑", 9)
            };
            leftPanel.Controls.Add(lblHelp);

            lblStatus = new Label
            {
                Text = "",
                Location = new Point(20, 310),
                Size = new Size(260, 40),
                Font = new Font("微软雅黑", 10, FontStyle.Bold),
                ForeColor = Color.SteelBlue
            };
            leftPanel.Controls.Add(lblStatus);

            gamePanel = new BufferedPanel
            {
                Location = new Point(360, 20),
                Size = new Size(1200, 1200),
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };
            gamePanel.Paint += GamePanel_Paint;

            Controls.AddRange(new Control[] { leftPanel, gamePanel });

            statusTimer.Interval = 2000;
            statusTimer.Tick += (s, e) => { lblStatus.Text = ""; statusTimer.Stop(); };
        }

        private void GenerateMaze()
        {
            int width, height;
            double twistBias;
            int extraPaths;

            if (difficulty <= 30)
            {
                width = 41; height = 31;
                twistBias = 0.0;
                extraPaths = 40;
            }
            else if (difficulty <= 60)
            {
                width = 71; height = 51;
                twistBias = (difficulty - 30) * 0.015;
                extraPaths = 30 - (difficulty - 30) / 3;
            }
            else
            {
                width = 101; height = 71;
                twistBias = 0.4 + (difficulty - 60) * 0.015;
                extraPaths = Math.Max(5, 20 - (difficulty - 60) / 4);
            }

            var generator = new MazeGenerator(width, height);
            generator.GeneratePerfectMaze(twistBias);
            generator.AddExtraPaths(extraPaths + 10);   // 增加通路
            generator.SetStartEndPoints();

            Random rnd = new Random();
            int idx = rnd.Next(4);
            playerPos = generator.StartPoints[idx];
            spawnPoint = playerPos;

            bool[] walls = generator.SerializeWalls();
            maze = new MazeData(generator.Width, generator.Height, walls,
                               generator.StartPoints, generator.EndPoint);
            explored = new bool[maze.Width, maze.Height];
            explored[playerPos.X, playerPos.Y] = true;
            MarkForwardMemory();

            // ⚠️ 确保 maze 已赋值后才调用路径计算
            FindPathNearCells();

            // 传送阵
            int tpCount = Math.Max(3, (width * height) / 200);
            teleporters.Clear();
            var allPositions = new List<Point>();
            for (int x = 1; x < maze.Width - 1; x++)
                for (int y = 1; y < maze.Height - 1; y++)
                    if (CanMove(new Point(x, y), x, y))
                        allPositions.Add(new Point(x, y));

            for (int i = allPositions.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var temp = allPositions[i];
                allPositions[i] = allPositions[j];
                allPositions[j] = temp;
            }
            int taken = 0;
            foreach (var p in allPositions)
            {
                if (taken >= tpCount) break;
                if (p == spawnPoint) continue;
                teleporters.Add(p);
                taken++;
            }

            // 金币仅在 nearPathCells 中生成
            int coinCount = (width * height) / 15;
            coins.Clear();
            var occupied = new HashSet<Point>(teleporters);
            occupied.Add(spawnPoint);
            List<Point> coinCandidates = nearPathCells
                .Where(p => CanMove(p, p.X, p.Y) && !occupied.Contains(p))
                .ToList();

            for (int i = coinCandidates.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var temp = coinCandidates[i];
                coinCandidates[i] = coinCandidates[j];
                coinCandidates[j] = temp;
            }
            taken = 0;
            foreach (var p in coinCandidates)
            {
                if (taken >= coinCount) break;
                coins.Add(new Coin { Position = p, Collected = false });
                taken++;
            }
        }

        private void FindPathNearCells()
        {
            nearPathCells.Clear();
            var queue = new Queue<Point>();
            var visited = new HashSet<Point>();
            var parent = new Dictionary<Point, Point>();
            queue.Enqueue(spawnPoint);
            visited.Add(spawnPoint);
            bool found = false;

            while (queue.Count > 0 && !found)
            {
                Point cur = queue.Dequeue();
                int[] dx = { 1, -1, 0, 0 };
                int[] dy = { 0, 0, 1, -1 };
                for (int i = 0; i < 4; i++)
                {
                    int nx = cur.X + dx[i];
                    int ny = cur.Y + dy[i];
                    Point next = new Point(nx, ny);
                    if (nx < 0 || nx >= maze.Width || ny < 0 || ny >= maze.Height)
                        continue;
                    if (!CanMove(cur, nx, ny) || visited.Contains(next))
                        continue;
                    visited.Add(next);
                    parent[next] = cur;
                    if (next == maze.EndPoint)
                    {
                        found = true;
                        break;
                    }
                    queue.Enqueue(next);
                }
            }

            var path = new HashSet<Point>();
            if (found)
            {
                Point cur = maze.EndPoint;
                while (true)
                {
                    path.Add(cur);
                    if (cur == spawnPoint) break;
                    cur = parent[cur];
                }
            }

            foreach (Point p in path)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > 2) continue;
                        int nx = p.X + dx;
                        int ny = p.Y + dy;
                        if (nx >= 0 && nx < maze.Width && ny >= 0 && ny < maze.Height)
                            nearPathCells.Add(new Point(nx, ny));
                    }
                }
            }
        }
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (pendingTeleport.HasValue)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    pendingTeleport = null;
                    ShowStatus("传送已取消");
                    gamePanel.Refresh();  // 改为 Refresh，立即刷新
                    return;
                }
                if (e.KeyCode == Keys.Space)
                {
                    Random rnd = new Random();
                    int idx = rnd.Next(teleporterTargets.Count);
                    Point dest = teleporterTargets.ElementAt(idx);
                    PerformTeleport(dest);
                    return;
                }
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
                    e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                {
                    Point best = teleporterTargets.First();
                    double bestScore = double.MaxValue;
                    int selDx = 0, selDy = 0;
                    switch (e.KeyCode)
                    {
                        case Keys.Up: selDy = -1; break;
                        case Keys.Down: selDy = 1; break;
                        case Keys.Left: selDx = -1; break;
                        case Keys.Right: selDx = 1; break;
                    }
                    foreach (var tp in teleporterTargets)
                    {
                        int tdx = tp.X - playerPos.X;
                        int tdy = tp.Y - playerPos.Y;
                        double dot = tdx * selDx + tdy * selDy;
                        double dist = Math.Sqrt(tdx * tdx + tdy * tdy);
                        double score = -dot / (dist + 0.1);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = tp;
                        }
                    }
                    PerformTeleport(best);
                }
                return;
            }

            if (finished) return;
            if (!gameStarted)
            {
                gameStarted = true;
                timer.Start();
                Timer t = new Timer { Interval = 100 };
                t.Tick += (s, ev) =>
                {
                    if (gameStarted && !finished)
                        lblTimer.Text = FormatTime(timer.Elapsed.TotalSeconds);
                };
                t.Start();
            }

            int dx = 0, dy = 0;
            switch (e.KeyCode)
            {
                case Keys.Up: dy = -1; playerDir = Direction.Up; break;
                case Keys.Down: dy = 1; playerDir = Direction.Down; break;
                case Keys.Left: dx = -1; playerDir = Direction.Left; break;
                case Keys.Right: dx = 1; playerDir = Direction.Right; break;
                default: return;
            }

            int newX = playerPos.X + dx, newY = playerPos.Y + dy;
            if (CanMove(playerPos, newX, newY))
            {
                playerPos = new Point(newX, newY);

                if (teleporters.Contains(playerPos))
                {
                    teleporterTargets.Clear();
                    foreach (var tp in teleporters)
                        if (tp != playerPos)
                            teleporterTargets.Add(tp);

                    pendingTeleport = playerPos;
                    ShowStatus("按方向键选目标\n空格随机传送，Esc 取消");
                    gamePanel.Refresh();  // 立即刷新以缩小显示
                    return;
                }

                explored[newX, newY] = true;
                MarkForwardMemory();
                CheckCoinPickup();
                if (newX == maze.EndPoint.X && newY == maze.EndPoint.Y)
                    FinishGame();
            }
            gamePanel.Refresh();  // 普通移动后也立即刷新
            baseCellSize = Math.Max(20,baseCellSizeNormal);
            e.Handled = true;
        }

        private void PerformTeleport(Point dest)
        {
            playerPos = dest;
            explored[dest.X, dest.Y] = true;
            pendingTeleport = null;
            MarkForwardMemory();
            CheckCoinPickup();
            if (playerPos.X == maze.EndPoint.X && playerPos.Y == maze.EndPoint.Y)
                FinishGame();
            ShowStatus("传送成功！");
            baseCellSize = Math.Max(baseCellSize / 3, 20);  
            gamePanel.Refresh();
        }

        private void ShowStatus(string msg)
        {
            lblStatus.Text = msg;
            statusTimer.Stop();
            statusTimer.Start();
        }

        private void CheckCoinPickup()
        {
            foreach (var coin in coins)
            {
                if (!coin.Collected && coin.Position == playerPos)
                {
                    coin.Collected = true;
                    score += coinValue;
                    coinsCollected++;
                    lblScore.Text = $"积分: {score}  金币: {coinsCollected}";
                }
            }
        }

        private void FinishGame()
        {
            finished = true;
            timer.Stop();
            lblTimer.Text = "通关！ " + FormatTime(timer.Elapsed.TotalSeconds);
            MessageBox.Show($"恭喜 {playerName} 到达终点！\n用时：{FormatTime(timer.Elapsed.TotalSeconds)}\n积分：{score}  金币：{coinsCollected}");
        }

        private bool CanMove(Point from, int toX, int toY)
        {
            if (toX < 0 || toX >= maze.Width || toY < 0 || toY >= maze.Height)
                return false;
            int ddx = toX - from.X;
            int ddy = toY - from.Y;
            if (ddx == 1 && maze.WallRight[from.X, from.Y]) return false;
            if (ddx == -1 && maze.WallLeft[from.X, from.Y]) return false;
            if (ddy == 1 && maze.WallBottom[from.X, from.Y]) return false;
            if (ddy == -1 && maze.WallTop[from.X, from.Y]) return false;
            return true;
        }

        private void MarkForwardMemory()
        {
            int fdx = 0, fdy = 0;
            switch (playerDir)
            {
                case Direction.Right: fdx = 1; break;
                case Direction.Up: fdy = -1; break;
                case Direction.Left: fdx = -1; break;
                case Direction.Down: fdy = 1; break;
            }

            int curX = playerPos.X;
            int curY = playerPos.Y;
            for (int i = 0; i < 3; i++)
            {
                int nextX = curX + fdx;
                int nextY = curY + fdy;
                if (nextX < 0 || nextX >= maze.Width || nextY < 0 || nextY >= maze.Height)
                    break;
                if (fdx == 1 && maze.WallRight[curX, curY]) break;
                if (fdx == -1 && maze.WallLeft[curX, curY]) break;
                if (fdy == 1 && maze.WallBottom[curX, curY]) break;
                if (fdy == -1 && maze.WallTop[curX, curY]) break;
                explored[nextX, nextY] = true;
                curX = nextX;
                curY = nextY;
            }
        }

        private void ComputeLightData()
        {
            List<PointF> vertices = new List<PointF>();
            HashSet<HitWall> hits = new HashSet<HitWall>();
            int cs = CurrentCellSize;

            float centerX = gamePanel.Width / 2f;
            float centerY = gamePanel.Height / 2f;
            vertices.Add(new PointF(centerX, centerY));

            double faceAngle = playerDir switch
            {
                Direction.Right => 0,
                Direction.Down => 90,
                Direction.Left => 180,
                Direction.Up => 270,
                _ => 0
            };

            const double step = 0.15;
            const int maxSteps = 1000;

            for (double offset = -halfAngle; offset <= halfAngle; offset += 0.25)
            {
                double rad = (faceAngle + offset) * Math.PI / 180.0;
                double rdx = Math.Cos(rad);
                double rdy = Math.Sin(rad);

                double wx = playerPos.X + 0.5;
                double wy = playerPos.Y + 0.5;
                int lastX = playerPos.X, lastY = playerPos.Y;
                int steps = 0;
                bool blocked = false;

                while (steps < maxSteps)
                {
                    wx += rdx * step;
                    wy += rdy * step;
                    steps++;
                    int curX = (int)Math.Floor(wx);
                    int curY = (int)Math.Floor(wy);
                    if (curX < 0 || curX >= maze.Width || curY < 0 || curY >= maze.Height)
                    { blocked = true; break; }

                    if (curX != lastX || curY != lastY)
                    {
                        if (curX != lastX)
                        {
                            if (curX > lastX && maze.WallRight[lastX, lastY])
                            { hits.Add(new HitWall(lastX, lastY, WallSide.Right)); blocked = true; }
                            else if (curX < lastX && maze.WallLeft[lastX, lastY])
                            { hits.Add(new HitWall(lastX, lastY, WallSide.Left)); blocked = true; }
                        }
                        if (!blocked && curY != lastY)
                        {
                            if (curY > lastY && maze.WallBottom[lastX, lastY])
                            { hits.Add(new HitWall(lastX, lastY, WallSide.Bottom)); blocked = true; }
                            else if (curY < lastY && maze.WallTop[lastX, lastY])
                            { hits.Add(new HitWall(lastX, lastY, WallSide.Top)); blocked = true; }
                        }
                        if (blocked)
                        {
                            wx -= rdx * step * 0.5;
                            wy -= rdy * step * 0.5;
                            break;
                        }
                        lastX = curX;
                        lastY = curY;
                    }
                }
                float vx = (float)((wx - (playerPos.X + 0.5)) * cs);
                float vy = (float)((wy - (playerPos.Y + 0.5)) * cs);
                vertices.Add(new PointF(centerX + vx, centerY + vy));
            }
            currentLightPolygon = vertices.ToArray();
            currentHitWalls = hits;
        }

        private HashSet<Point> GetVisibleCells()
        {
            HashSet<Point> visible = new HashSet<Point>();
            visible.Add(playerPos);

            double faceAngle = playerDir switch
            {
                Direction.Right => 0,
                Direction.Down => 90,
                Direction.Left => 180,
                Direction.Up => 270,
                _ => 0
            };
            const double step = 0.15;
            const int maxSteps = 1000;

            for (double offset = -halfAngle; offset <= halfAngle; offset += 0.5)
            {
                double rad = (faceAngle + offset) * Math.PI / 180.0;
                double vdx = Math.Cos(rad);
                double vdy = Math.Sin(rad);
                double wx = playerPos.X + 0.5, wy = playerPos.Y + 0.5;
                int lastX = playerPos.X, lastY = playerPos.Y;
                int steps = 0;
                bool blocked = false;

                while (steps < maxSteps)
                {
                    wx += vdx * step;
                    wy += vdy * step;
                    steps++;
                    int curX = (int)Math.Floor(wx), curY = (int)Math.Floor(wy);
                    if (curX < 0 || curX >= maze.Width || curY < 0 || curY >= maze.Height)
                        break;
                    if (curX != lastX || curY != lastY)
                    {
                        if (curX != lastX)
                        {
                            if ((curX > lastX && maze.WallRight[lastX, lastY]) ||
                                (curX < lastX && maze.WallLeft[lastX, lastY]))
                            { blocked = true; break; }
                        }
                        if (curY != lastY)
                        {
                            if ((curY > lastY && maze.WallBottom[lastX, lastY]) ||
                                (curY < lastY && maze.WallTop[lastX, lastY]))
                            { blocked = true; break; }
                        }
                        visible.Add(new Point(curX, curY));
                        lastX = curX; lastY = curY;
                    }
                }
            }
            return visible;
        }

        private void GamePanel_Paint(object sender, PaintEventArgs e)
        {
            if (maze == null) return;
            Graphics g = e.Graphics;
            g.Clear(Color.Black);

            int cs = CurrentCellSize; // 动态尺寸

            ComputeLightData();
            HashSet<Point> visibleCells = GetVisibleCells();
            foreach (Point p in visibleCells)
                explored[p.X, p.Y] = true;

            float centerX = gamePanel.Width / 2f;
            float centerY = gamePanel.Height / 2f;
            float offsetX = centerX - (playerPos.X + 0.5f) * cs;
            float offsetY = centerY - (playerPos.Y + 0.5f) * cs;

            int worldLeft = (int)Math.Floor((-offsetX) / cs);
            int worldTop = (int)Math.Floor((-offsetY) / cs);
            int worldRight = (int)Math.Ceiling((gamePanel.Width - offsetX) / cs);
            int worldBottom = (int)Math.Ceiling((gamePanel.Height - offsetY) / cs);
            int startX = Math.Max(0, worldLeft);
            int startY = Math.Max(0, worldTop);
            int endX = Math.Min(maze.Width - 1, worldRight);
            int endY = Math.Min(maze.Height - 1, worldBottom);

            // 1. 记忆地板与灰色墙壁
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    if (!explored[x, y]) continue;
                    int sx = (int)(offsetX + x * cs);
                    int sy = (int)(offsetY + y * cs);

                    g.FillRectangle(new SolidBrush(Color.FromArgb(25, 25, 25)), sx, sy, cs, cs);

                    int thick = cs / 4;
                    int half = thick / 2;
                    Pen grayPen = new Pen(Color.Gray, thick);
                    if (maze.WallTop[x, y]) g.DrawLine(grayPen, sx - half, sy, sx + cs + half, sy);
                    if (maze.WallLeft[x, y]) g.DrawLine(grayPen, sx, sy - half, sx, sy + cs + half);
                    if (maze.WallRight[x, y]) g.DrawLine(grayPen, sx + cs, sy - half, sx + cs, sy + cs + half);
                    if (maze.WallBottom[x, y]) g.DrawLine(grayPen, sx - half, sy + cs, sx + cs + half, sy + cs);
                }
            }

            // 2. 光锥
            if (currentLightPolygon.Length > 2)
            {
                using (Brush lightBrush = new SolidBrush(Color.FromArgb(120, 255, 240, 200)))
                    g.FillPolygon(lightBrush, currentLightPolygon);
            }

            // 3. 高亮墙壁
            if (currentHitWalls != null)
            {
                int thick = cs / 4;
                int half = thick / 2;
                Pen lightPen = new Pen(Color.FromArgb(255, 255, 240, 200), thick);
                foreach (HitWall hw in currentHitWalls)
                {
                    int sx = (int)(offsetX + hw.X * cs);
                    int sy = (int)(offsetY + hw.Y * cs);
                    if (sx + cs < 0 || sx > gamePanel.Width || sy + cs < 0 || sy > gamePanel.Height)
                        continue;
                    switch (hw.Side)
                    {
                        case WallSide.Top: g.DrawLine(lightPen, sx - half, sy, sx + cs + half, sy); break;
                        case WallSide.Left: g.DrawLine(lightPen, sx, sy - half, sx, sy + cs + half); break;
                        case WallSide.Bottom: g.DrawLine(lightPen, sx - half, sy + cs, sx + cs + half, sy + cs); break;
                        case WallSide.Right: g.DrawLine(lightPen, sx + cs, sy - half, sx + cs, sy + cs + half); break;
                    }
                }
            }

            // 4. 出口标记
            Point end = maze.EndPoint;
            if (explored[end.X, end.Y])
            {
                int ex = (int)(offsetX + end.X * cs);
                int ey = (int)(offsetY + end.Y * cs);
                int margin = cs / 8;
                g.FillRectangle(Brushes.LimeGreen, ex + margin, ey + margin, cs - margin * 2, cs - margin * 2);
            }

            // 5. 传送阵标记
            foreach (var tp in teleporters)
            {
                if (!explored[tp.X, tp.Y]) continue;
                int tx = (int)(offsetX + tp.X * cs);
                int ty = (int)(offsetY + tp.Y * cs);
                Point[] diamond = {
                    new Point(tx + cs/2, ty + 2),
                    new Point(tx + cs - 2, ty + cs/2),
                    new Point(tx + cs/2, ty + cs - 2),
                    new Point(tx + 2, ty + cs/2)
                };
                g.FillPolygon(Brushes.MediumPurple, diamond);
                if (pendingTeleport.HasValue && teleporterTargets.Contains(tp))
                {
                    g.DrawEllipse(new Pen(Color.Yellow, 2), tx + 4, ty + 4, cs - 8, cs - 8);
                }
            }

            // 6. 出生点标记
            if (explored[spawnPoint.X, spawnPoint.Y])
            {
                int spawnX = (int)(offsetX + spawnPoint.X * cs);
                int spawnY = (int)(offsetY + spawnPoint.Y * cs);
                int arrowSize = cs / 3;
                Point[] arrow = new Point[]
                {
                    new Point(spawnX + cs/2, spawnY + cs/2 - arrowSize),
                    new Point(spawnX + cs/2 - arrowSize, spawnY + cs/2 + arrowSize/2),
                    new Point(spawnX + cs/2 + arrowSize, spawnY + cs/2 + arrowSize/2)
                };
                g.FillPolygon(Brushes.DodgerBlue, arrow);
            }

            // 7. 金币绘制
            foreach (var coin in coins)
            {
                if (coin.Collected) continue;
                Point coinPos = coin.Position;
                if (!visibleCells.Contains(coinPos) && !explored[coinPos.X, coinPos.Y]) continue; // 看不见

                int cx = (int)(offsetX + coinPos.X * cs) + cs / 2;
                int cy = (int)(offsetY + coinPos.Y * cs) + cs / 2;
                int r = cs / 6;

                bool inLight = visibleCells.Contains(coinPos);
                Color fillColor = inLight ? Color.Gold : Color.FromArgb(100, Color.DarkGoldenrod);
                Color borderColor = inLight ? Color.DarkGoldenrod : Color.FromArgb(100, Color.DarkGoldenrod);
                Color textColor = inLight ? Color.DarkGoldenrod : Color.FromArgb(120, Color.Goldenrod);

                g.FillEllipse(new SolidBrush(fillColor), cx - r, cy - r, r * 2, r * 2);
                g.DrawEllipse(new Pen(borderColor, 1), cx - r, cy - r, r * 2, r * 2);
                // 只有光照下才画高光和 "$"
                if (inLight)
                {
                    g.DrawArc(new Pen(Color.White, 1), cx - r + 2, cy - r + 2, r * 2 - 4, r * 2 - 4, 200, 100);
                    using (Font coinFont = new Font("Arial", r, FontStyle.Bold))
                    {
                        StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString("$", coinFont, Brushes.DarkGoldenrod, new Rectangle(cx - r, cy - r, r * 2, r * 2), sf);
                    }
                }
                else
                {
                    // 记忆中的金币：只画一个暗淡的 "$"
                    using (Font coinFont = new Font("Arial", (int)(r * 0.8), FontStyle.Bold))
                    {
                        StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString("$", coinFont, new SolidBrush(textColor), new Rectangle(cx - r, cy - r, r * 2, r * 2), sf);
                    }
                }
            }

            // 8. 玩家
            float px = centerX, py = centerY;
            int pr = cs / 5;
            g.FillEllipse(Brushes.Red, px - pr, py - pr, pr * 2, pr * 2);
            int arrowLen = cs / 6;
            Pen dirPen = new Pen(Color.Yellow, 2);
            switch (playerDir)
            {
                case Direction.Right: g.DrawLine(dirPen, px, py, px + 10, py); break;
                case Direction.Down: g.DrawLine(dirPen, px, py, px, py + 10); break;
                case Direction.Left: g.DrawLine(dirPen, px, py, px - 10, py); break;
                case Direction.Up: g.DrawLine(dirPen, px, py, px, py - 10); break;
            }
        }

        private string FormatTime(double sec) => TimeSpan.FromSeconds(sec).ToString(@"mm\:ss\.f");
    }
}