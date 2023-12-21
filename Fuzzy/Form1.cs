using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Fuzzy
{
    public partial class Form1 : Form
    {
        private const int CellSize = 40;

        private Field field;
        private Robot robot;
        private PictureBox pictureBox;
        private Timer timer;

        public Form1()
        {
            Text = "Fuzzy Robot Control";
            ClientSize = new Size(CellSize * Robot.NumCellsX, CellSize * Robot.NumCellsY);
            
            field = new Field(Robot.NumCellsX, Robot.NumCellsY);
            field.RandomlyFillObstacles(field, 20);
            robot = new Robot(field);
            robot.x = 0;
            robot.y = 0;


            pictureBox = new PictureBox();
            pictureBox.Dock = DockStyle.Fill;
            pictureBox.Paint += PictureBox_Paint;
            Controls.Add(pictureBox);

            timer = new Timer();
            timer.Interval = 500;
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            for (int x = 0; x < Robot.NumCellsX; x++)
            {
                for (int y = 0; y < Robot.NumCellsY; y++)
                {
                    int cellX = x * CellSize;
                    int cellY = y * CellSize;

                    Brush brush = field.IsObstacle(x, y) ? Brushes.Red : Brushes.White;
                    g.FillRectangle(brush, cellX, cellY, CellSize, CellSize);
                    g.DrawRectangle(Pens.Black, cellX, cellY, CellSize, CellSize);
                }
            }

            int robotX = robot.x * CellSize + CellSize / 2;
            int robotY = robot.y * CellSize + CellSize / 2;
            g.FillEllipse(Brushes.Blue, robotX - CellSize / 4, robotY - CellSize / 4, CellSize / 2, CellSize / 2);
        }

        

        private async void Timer_Tick(object sender, EventArgs e)
        {
            await Task.Run(() =>
            {
                robot.Move();
                pictureBox.Invalidate();
            });
        }
    }

    public class Robot
    {
        public const int NumCellsX = 10;
        public const int NumCellsY = 10;

        private Field field;
        private HistoryStepsFuzzy historyStep = new HistoryStepsFuzzy();
        private int priorityDirection = Side.bottom;
        public int x;
        public int y;

        public Robot(Field field)
        {
            this.field = field;
        }



        public event Action<int, int> MoveEvent;

        private void SetCoord(int x, int y)
        {
            MoveEvent?.Invoke(x, y);
            historyStep.Push(x, y);
        }

        public void Move()
        {
            double collisionTop = 0;
            double collisionRight = 0;
            double collisionBottom = 0;
            double collisionLeft = 0;

            double lastMoveTop = historyStep.GetLastStep(x, y - 1);
            double lastMoveRight = historyStep.GetLastStep(x + 1, y);
            double lastMoveBottom = historyStep.GetLastStep(x, y + 1);
            double lastMoveLeft = historyStep.GetLastStep(x - 1, y);

            var result = ProcessObstacleDistancesAndDirections(collisionTop, collisionRight, collisionBottom, collisionLeft);

            double weightTop = GetPriorityDirection(Side.top, Side.bottom, result.collisionTop, lastMoveTop);
            double weightRight = GetPriorityDirection(Side.right, Side.left, result.collisionRight, lastMoveRight);
            double weightBottom = GetPriorityDirection(Side.bottom, Side.top, result.collisionBottom, lastMoveBottom);
            double weightLeft = GetPriorityDirection(Side.left, Side.right, result.collisionLeft, lastMoveLeft);

            setNextCoord(weightTop, weightRight, weightBottom, weightLeft);
        }

        private double GetPriorityDirection(int toward, int behind, double obstruction, double recency)
        {
            return (priorityDirection == toward ? 2 : priorityDirection == behind ? 1 : 1.5) / obstruction / (0.1 + recency);
        }

        private void setNextCoord(double top, double right, double bottom, double left)
        {
            double maxWeigth = Math.Max(Math.Max(top, right), Math.Max(bottom, left));

            _ = maxWeigth == top ? y -= 1 : maxWeigth == right ? x += 1 : maxWeigth == bottom ? y += 1 : x -= 1;
            SetCoord(x, y);
        }

        public (double collisionTop, double collisionRight, double collisionBottom, double collisionLeft) ProcessObstacleDistancesAndDirections(double initialCollisionTop, double initialCollisionRight, double initialCollisionBottom, double initialCollisionLeft)
        {
            double collisionTop = initialCollisionTop;
            double collisionRight = initialCollisionRight;
            double collisionBottom = initialCollisionBottom;
            double collisionLeft = initialCollisionLeft;

            foreach (var (distance, direction) in GetFieldStatus())
            {
                var proximity = DistanceToWallFuzzy.GetDistanceToWall(distance);
                if (proximity > 0.999) proximity *= 10000;

                collisionTop += proximity * ObstacleFuzzyLogic.GetСoefficientTop(direction);
                collisionRight += proximity * ObstacleFuzzyLogic.GetСoefficientRight(direction);
                collisionBottom += proximity * ObstacleFuzzyLogic.GetСoefficientBottom(direction);
                collisionLeft += proximity * ObstacleFuzzyLogic.GetСoefficientLeft(direction);
            }

            return (collisionTop, collisionRight, collisionBottom, collisionLeft);
        }

        private IEnumerable<(double dist, double dir)> GetFieldStatus()
        {
            foreach (var (x, y) in field.GetObstacles())
            {
                var dist = (double)Math.Sqrt(Math.Pow(x - this.x, 2) + Math.Pow(y - this.y, 2));
                var dir = (double)Math.Atan2(x - this.x, y - this.y);
                yield return (dist, dir);
            }
        }
    }

    public class Side
    {
        public const int top = 0;
        public const int right = 1;
        public const int bottom = 2;
        public const int left = 3;
    }

    public class Field
    {
        public readonly int X;
        public readonly int Y;

        private readonly bool[,] obstacles;
        public readonly Robot Robot;

        public Field(int x, int y)
        {
            X = x;
            Y = y;
            obstacles = new bool[x, y];
            Robot = new Robot(this);
        }

        public void SetObstacle(int x, int y, bool obstacle)
        {
            obstacles[x, y] = obstacle;
        }

        public void RandomlyFillObstacles(Field field, int numObstacles)
        {
            Random random = new Random();

            int obstaclesPlaced = 0;
            while (obstaclesPlaced < numObstacles)
            {
                int x = random.Next(0, field.X);
                int y = random.Next(0, field.Y);

                if (x == 0 && y == 0) continue;

                if (!field.IsObstacle(x, y))
                {
                    field.SetObstacle(x, y, true);
                    obstaclesPlaced++;
                }
            }
        }

        public bool IsObstacle(int x, int y)
        {
            return obstacles[x, y];
        }


        public IEnumerable<(int x, int y)> GetObstacles()
        {
            foreach (var obstacle in GetInnerObstacles())
            {
                yield return obstacle;
            }

            foreach (var borderPoint in GetBorderPoints())
            {
                yield return borderPoint;
            }
        }

        private IEnumerable<(int x, int y)> GetInnerObstacles()
        {
            for (var x = 0; x < X; x++)
            {
                for (var y = 0; y < Y; y++)
                {
                    if (obstacles[x, y])
                    {
                        yield return (x, y);
                    }
                }
            }
        }

        private IEnumerable<(int x, int y)> GetBorderPoints()
        {
            for (var x = 0; x < X; x++)
            {
                yield return (x, -1);
                yield return (x, Y);
            }

            for (var y = 0; y < Y; y++)
            {
                yield return (-1, y);
                yield return (X, y);
            }
        }
    }

    public class HistoryStepsFuzzy
    {
        private const int MaxHistoryLength = 15;
        private readonly List<(int x, int y)> history = new List<(int x, int y)>(MaxHistoryLength);

        private static readonly Fuzzyfication History = new Fuzzyfication(0, MaxHistoryLength, double.PositiveInfinity, double.PositiveInfinity);

        public void Push(int x, int y)
        {
            if (history.Count == MaxHistoryLength) history.RemoveAt(0);
            history.Add((x, y));
        }

        public double GetLastStep(int x, int y)
        {
            var index = history.IndexOf((x, y));
            return History.TrapezoidalMembershipFunction(index + 1);
        }
    }

    public class Fuzzyfication
    {
        public Func<double, double> TrapezoidalMembershipFunction { get; }

        public Fuzzyfication(double a, double b, double c, double d)
        {
            TrapezoidalMembershipFunction = value =>
            {
                if (value < a || value > d) return 0;
                if (value > b && value < c) return 1;
                if (value < b) return (value - a) / (b - a);
                return (c - value) / (d - c) + 1;
            };
        }
    }

    public class DistanceToWallFuzzy
    {
        private static readonly Fuzzyfication Close = new Fuzzyfication(0, 0, 1, 5);

        public static double GetDistanceToWall(double dist)
        {
            return Close.TrapezoidalMembershipFunction(dist);
        }
    }

    public static class ObstacleFuzzyLogic
    {
        private static Fuzzyfication Top = new Fuzzyfication(-1.57, 0, 0, 1.57);
        private static Fuzzyfication Right = new Fuzzyfication(0, 1.57, 1.57, 3.14);
        private static Fuzzyfication Bottom = new Fuzzyfication(1.57, 3.14, 3.14, 4.71);
        private static Fuzzyfication Left = new Fuzzyfication(-3.14, -1.57, -1.57, 0);

        public static double GetСoefficientTop(double angle)
        {
            return Bottom.TrapezoidalMembershipFunction(angle >= 0 ? angle : angle + 6.28);
        }
        public static double GetСoefficientRight(double angle)
        {
            return Right.TrapezoidalMembershipFunction(angle);
        }
        public static double GetСoefficientBottom(double angle)
        {
            return Top.TrapezoidalMembershipFunction(angle);
        }
        public static double GetСoefficientLeft(double angle)
        {
            return Left.TrapezoidalMembershipFunction(angle);
        }
}
}