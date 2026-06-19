using System.Drawing;

namespace MazeRace
{
    public class MazeData
    {
        public int Width { get; private set; }
        public int Height { get; private set; }

        public bool[,] WallLeft;
        public bool[,] WallTop;
        public bool[,] WallRight;
        public bool[,] WallBottom;

        public Point[] StartPoints { get; private set; }
        public Point EndPoint { get; private set; }

        public MazeData(int width, int height, bool[] serializedWalls,
                        Point[] startPoints, Point endPoint)
        {
            Width = width;
            Height = height;
            StartPoints = startPoints;
            EndPoint = endPoint;

            WallRight = new bool[width, height];
            WallBottom = new bool[width, height];

            int idx = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    WallRight[x, y] = serializedWalls[idx++];
                    WallBottom[x, y] = serializedWalls[idx++];
                }
            }

            // 推导左墙和上墙
            WallLeft = new bool[width, height];
            WallTop = new bool[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    WallLeft[x, y] = (x == 0) ? true : WallRight[x - 1, y];
                    WallTop[x, y] = (y == 0) ? true : WallBottom[x, y - 1];
                }
            }

            // 根据出生点打开左墙
            foreach (var sp in startPoints)
            {
                if (sp.X == 0)
                    WallLeft[0, sp.Y] = false;
            }

            // 出口打开右墙
            if (endPoint.X == width - 1)
                WallRight[width - 1, endPoint.Y] = false;
        }
    }
}