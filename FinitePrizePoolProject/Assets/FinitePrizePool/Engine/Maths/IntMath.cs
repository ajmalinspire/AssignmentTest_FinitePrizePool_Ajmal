namespace FinitePrizePool.Engine.Maths
{
    public static class IntMath
    {
        public static long Gcd(long a, long b)
        {
            if (a < 0) a = -a;
            if (b < 0) b = -b;
            while (b != 0)
            {
                var t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        /// <summary>⌈a ÷ b⌉ for a ≥ 0, b &gt; 0.</summary>
        public static long CeilDiv(long a, long b) => (a + b - 1) / b;

        /// <summary>⌊a ÷ b⌋ for a ≥ 0, b &gt; 0.</summary>
        public static long FloorDiv(long a, long b) => a / b;
    }
}
