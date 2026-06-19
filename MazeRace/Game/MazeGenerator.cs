using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MazeRace
{
    public class MazeCell
    {
        public bool WallTop = true, WallBottom = true, WallLeft = true, WallRight = true;
        public bool Visited = false;
    }

    public class MazeGenerator
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public MazeCell[,] Cells { get; private set; }
        public Point[] StartPoints { get; private set; }
        public Point EndPoint { get; private set; }
        private Random rand = new Random();

        // 新增字段用于曲折度控制
        private Direction? lastDir = null;

        public MazeGenerator(int w, int h)
        {
            Width = w; Height = h;
            Cells = new MazeCell[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    Cells[x, y] = new MazeCell();
        }

        // 生成完美迷宫，twistBias 范围 0~1，0=完全随机，1=最大曲折
        public void GeneratePerfectMaze(double twistBias = 0.0)
        {
            var stack = new Stack<(int x, int y)>();
            var cur = (x: 0, y: 0);
            Cells[0, 0].Visited = true;
            stack.Push(cur);
            lastDir = null;

            while (stack.Count > 0)
            {
                cur = stack.Peek();
                var neighbors = GetUnvisited(cur.x, cur.y);
                if (neighbors.Count > 0)
                {
                    // 根据曲折度加权选择邻居
                    var next = ChooseWeightedNeighbor(neighbors, cur.x, cur.y, twistBias);
                    // 更新方向
                    int dx = next.x - cur.x, dy = next.y - cur.y;
                    if (dx == 1) lastDir = Direction.Right;
                    else if (dx == -1) lastDir = Direction.Left;
                    else if (dy == 1) lastDir = Direction.Down;
                    else if (dy == -1) lastDir = Direction.Up;

                    RemoveWall(cur.x, cur.y, next.x, next.y);
                    Cells[next.x, next.y].Visited = true;
                    stack.Push(next);
                }
                else stack.Pop();
            }
        }

        private (int x, int y) ChooseWeightedNeighbor(List<(int x, int y)> neighbors, int cx, int cy, double twistBias)
        {
            int curDx = 0, curDy = 0;
            if (lastDir.HasValue)
            {
                switch (lastDir.Value)
                {
                    case Direction.Right: curDx = 1; break;
                    case Direction.Up: curDy = -1; break;
                    case Direction.Left: curDx = -1; break;
                    case Direction.Down: curDy = 1; break;
                }
            }

            double[] weights = new double[neighbors.Count];
            double total = 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                int nx = neighbors[i].x, ny = neighbors[i].y;
                int dx = nx - cx, dy = ny - cy;
                bool sameDir = (dx == curDx && dy == curDy);
                // 相同方向权重低（曲折时鼓励转弯），不同方向权重高
                double w = sameDir ? (1.0 - twistBias) : (1.0 + twistBias);
                weights[i] = w;
                total += w;
            }
            double r = rand.NextDouble() * total;
            double cumulative = 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                cumulative += weights[i];
                if (r <= cumulative) return neighbors[i];
            }
            return neighbors.Last();
        }

        // 额外打通墙壁（增加环路）
        public void AddExtraPaths(int count)
        {
            for (int i = 0; i < count; i++)
            {
                int x = rand.Next(Width), y = rand.Next(Height);
                var dirs = new List<(int dx, int dy)> { (1, 0), (-1, 0), (0, 1), (0, -1) };
                var d = dirs[rand.Next(dirs.Count)];
                int nx = x + d.dx, ny = y + d.dy;
                if (nx >= 0 && nx < Width && ny >= 0 && ny < Height)
                    RemoveWall(x, y, nx, ny);
            }
        }

        // 以下方法保持不变：SetStartEndPoints, SerializeWalls, RemoveWall, GetUnvisited
        public void SetStartEndPoints()
        {
            var yCoords = Enumerable.Range(0, Height).OrderBy(y => rand.Next()).Take(4).ToArray();
            StartPoints = new Point[4];
            for (int i = 0; i < 4; i++)
            {
                StartPoints[i] = new Point(0, yCoords[i]);
                Cells[0, yCoords[i]].WallLeft = false;
            }
            int ey = rand.Next(Height);
            EndPoint = new Point(Width - 1, ey);
            Cells[Width - 1, ey].WallRight = false;
        }

        public bool[] SerializeWalls()
        {
            bool[] walls = new bool[Width * Height * 2];
            int idx = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    walls[idx++] = Cells[x, y].WallRight;
                    walls[idx++] = Cells[x, y].WallBottom;
                }
            return walls;
        }

        private void RemoveWall(int x1, int y1, int x2, int y2)
        {
            if (x2 == x1 + 1) { Cells[x1, y1].WallRight = false; Cells[x2, y2].WallLeft = false; }
            else if (x2 == x1 - 1) { Cells[x1, y1].WallLeft = false; Cells[x2, y2].WallRight = false; }
            else if (y2 == y1 + 1) { Cells[x1, y1].WallBottom = false; Cells[x2, y2].WallTop = false; }
            else if (y2 == y1 - 1) { Cells[x1, y1].WallTop = false; Cells[x2, y2].WallBottom = false; }
        }

        private List<(int x, int y)> GetUnvisited(int x, int y)
        {
            var res = new List<(int, int)>();
            if (x > 0 && !Cells[x - 1, y].Visited) res.Add((x - 1, y));
            if (x < Width - 1 && !Cells[x + 1, y].Visited) res.Add((x + 1, y));
            if (y > 0 && !Cells[x, y - 1].Visited) res.Add((x, y - 1));
            if (y < Height - 1 && !Cells[x, y + 1].Visited) res.Add((x, y + 1));
            return res;
        }
    }

    // Direction 枚举（若已存在可忽略）
    //public enum Direction { Right, Up, Left, Down }
}