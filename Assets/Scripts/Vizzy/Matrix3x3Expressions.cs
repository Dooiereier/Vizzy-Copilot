using System;
using System.Collections.Generic;
using ModApi.Craft.Program;

namespace Assets.Scripts.Vizzy
{
    /// <summary>
    /// 3x3 矩阵工具。矩阵用行主序 9 元素列表表示：
    /// [m00, m01, m02, m10, m11, m12, m20, m21, m22]，即 index = row * 3 + col。
    /// </summary>
    public static class Matrix3x3Math
    {
        public const int Dimension = 3;
        public const int Size = 9;
        private const double Epsilon = 1e-12;

        public static double[] Identity()
        {
            return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        }

        /// <summary>C = A * B</summary>
        public static double[] Multiply(double[] a, double[] b)
        {
            var c = new double[Size];
            for (int i = 0; i < Dimension; i++)
            {
                for (int j = 0; j < Dimension; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < Dimension; k++)
                    {
                        sum += a[i * Dimension + k] * b[k * Dimension + j];
                    }
                    c[i * Dimension + j] = sum;
                }
            }
            return c;
        }

        public static double GetElement(double[] m, int row, int col)
        {
            return m[row * Dimension + col];
        }

        /// <summary>返回把 M[row][col] 替换为 value 后的新矩阵</summary>
        public static double[] SetElement(double[] m, int row, int col, double value)
        {
            var copy = (double[])m.Clone();
            copy[row * Dimension + col] = value;
            return copy;
        }

        /// <summary>
        /// 归一化：mode 0 = 逐列归一化，mode 1 = 逐行归一化，mode 2 = Frobenius 整体缩放到模长 1。
        /// 零向量保持原样，避免除零。
        /// </summary>
        public static double[] Normalize(double[] m, int mode)
        {
            var r = (double[])m.Clone();
            switch (mode)
            {
                case 0:
                    for (int j = 0; j < Dimension; j++)
                    {
                        NormalizeVector(r, 0 * Dimension + j, 1 * Dimension + j, 2 * Dimension + j);
                    }
                    break;
                case 1:
                    for (int i = 0; i < Dimension; i++)
                    {
                        NormalizeVector(r, i * Dimension + 0, i * Dimension + 1, i * Dimension + 2);
                    }
                    break;
                default:
                    double squares = 0;
                    for (int i = 0; i < Size; i++)
                    {
                        squares += r[i] * r[i];
                    }
                    double norm = Math.Sqrt(squares);
                    if (norm > Epsilon)
                    {
                        for (int i = 0; i < Size; i++)
                        {
                            r[i] /= norm;
                        }
                    }
                    break;
            }
            return r;
        }

        private static void NormalizeVector(double[] m, int i0, int i1, int i2)
        {
            double squares = m[i0] * m[i0] + m[i1] * m[i1] + m[i2] * m[i2];
            double norm = Math.Sqrt(squares);
            if (norm <= Epsilon)
            {
                return;
            }
            m[i0] /= norm;
            m[i1] /= norm;
            m[i2] /= norm;
        }

        public static List<ExpressionListItem> ToList(double[] m)
        {
            var items = new List<ExpressionListItem>(Size);
            for (int i = 0; i < Size; i++)
            {
                items.Add(new ExpressionListItem { NumberValue = m[i] });
            }
            return items;
        }

        public static double[] FromList(IReadOnlyList<ExpressionListItem> list)
        {
            var m = new double[Size];
            int count = Math.Min(list.Count, Size);
            for (int i = 0; i < count; i++)
            {
                m[i] = list[i].NumberValue;
            }
            return m;
        }

        /// <summary>读取第 index 个表达式输入，必须是 9 元素列表，否则报错并回退为单位阵。</summary>
        public static double[] ReadMatrixInput(IThreadContext context, ProgramNode node, int index)
        {
            ProgramExpression expr = node.GetExpression(index);
            if (expr == null)
            {
                context.Log.LogError("Matrix node " + node.Style + ": missing expression " + index, context, node);
                return Identity();
            }
            IReadOnlyList<ExpressionListItem> list = expr.Evaluate(context).ListValue;
            if (list == null || list.Count < Size)
            {
                context.Log.LogError("Matrix node " + node.Style + ": expression " + index + " must be a 9-element list (got " + (list == null ? 0 : list.Count) + ")", context, node);
                return Identity();
            }
            return FromList(list);
        }

        public static double ReadNumberInput(IThreadContext context, ProgramNode node, int index)
        {
            ProgramExpression expr = node.GetExpression(index);
            if (expr == null)
            {
                context.Log.LogError("Matrix node " + node.Style + ": missing number expression " + index, context, node);
                return 0;
            }
            return expr.Evaluate(context).NumberValue;
        }

        /// <summary>读取索引类输入（row/col/mode），越界时报错并收敛到 [0, 2]。</summary>
        public static int ReadIndexInput(IThreadContext context, ProgramNode node, int index, string name)
        {
            int value = (int)Math.Round(ReadNumberInput(context, node, index));
            if (value < 0 || value > 2)
            {
                context.Log.LogError("Matrix node " + node.Style + ": " + name + " must be 0, 1 or 2 (got " + value + ")", context, node);
                return Math.Max(0, Math.Min(2, value));
            }
            return value;
        }
    }

    /// <summary>矩阵乘法：C = A * B。两个输入均为 9 元素行主序列表。</summary>
    [Serializable]
    public class Matrix3x3MulExpression : ProgramExpression
    {
        private readonly ExpressionResult _result = new ExpressionResult();

        public override bool IsBoolean
        {
            get { return false; }
        }

        public override ExpressionResult Evaluate(IThreadContext context)
        {
            double[] a = Matrix3x3Math.ReadMatrixInput(context, this, 0);
            double[] b = Matrix3x3Math.ReadMatrixInput(context, this, 1);
            _result.Set(new ExpressionResult(Matrix3x3Math.ToList(Matrix3x3Math.Multiply(a, b))));
            return _result;
        }
    }

    /// <summary>读取矩阵元素：返回 M[row][col]（0 基）。</summary>
    [Serializable]
    public class Matrix3x3GetElementExpression : ProgramExpression
    {
        private readonly ExpressionResult _result = new ExpressionResult();

        public override bool IsBoolean
        {
            get { return false; }
        }

        public override ExpressionResult Evaluate(IThreadContext context)
        {
            double[] m = Matrix3x3Math.ReadMatrixInput(context, this, 0);
            int row = Matrix3x3Math.ReadIndexInput(context, this, 1, "row");
            int col = Matrix3x3Math.ReadIndexInput(context, this, 2, "col");
            _result.NumberValue = Matrix3x3Math.GetElement(m, row, col);
            return _result;
        }
    }

    /// <summary>写入矩阵元素：返回把 M[row][col] 替换为 value 后的新矩阵。</summary>
    [Serializable]
    public class Matrix3x3SetElementExpression : ProgramExpression
    {
        private readonly ExpressionResult _result = new ExpressionResult();

        public override bool IsBoolean
        {
            get { return false; }
        }

        public override ExpressionResult Evaluate(IThreadContext context)
        {
            double[] m = Matrix3x3Math.ReadMatrixInput(context, this, 0);
            int row = Matrix3x3Math.ReadIndexInput(context, this, 1, "row");
            int col = Matrix3x3Math.ReadIndexInput(context, this, 2, "col");
            double value = Matrix3x3Math.ReadNumberInput(context, this, 3);
            _result.Set(new ExpressionResult(Matrix3x3Math.ToList(Matrix3x3Math.SetElement(m, row, col, value))));
            return _result;
        }
    }

    /// <summary>归一化：Mode 0 = 逐列，1 = 逐行，2 = Frobenius 整体缩放。</summary>
    [Serializable]
    public class Matrix3x3NormalizeExpression : ProgramExpression
    {
        private readonly ExpressionResult _result = new ExpressionResult();

        public override bool IsBoolean
        {
            get { return false; }
        }

        public override ExpressionResult Evaluate(IThreadContext context)
        {
            double[] m = Matrix3x3Math.ReadMatrixInput(context, this, 0);
            int mode = Matrix3x3Math.ReadIndexInput(context, this, 1, "mode");
            _result.Set(new ExpressionResult(Matrix3x3Math.ToList(Matrix3x3Math.Normalize(m, mode))));
            return _result;
        }
    }
}
