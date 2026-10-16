using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace FinitePrizePool.Engine.Core
{
    /// <summary>
    /// The only source of randomness in the engine. It is used while a deal is being built, never during a spin.
    /// </summary>
    public interface ISecureRandom
    {
        /// <summary>A uniformly random integer in [0, maxExclusive).</summary>
        int NextInt(int maxExclusive);
    }

    /// <summary>Backed by <see cref="RandomNumberGenerator.GetInt32(int)"/>, as the brief requires (§3.2).</summary>
    public sealed class CryptoSecureRandom : ISecureRandom
    {
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return RandomNumberGenerator.GetInt32(maxExclusive);
        }
    }

    public static class FisherYates
    {
        /// <summary>In-place Fisher–Yates shuffle: every ordering is equally likely.</summary>
        public static void Shuffle<T>(IList<T> items, ISecureRandom random)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = random.NextInt(i + 1);
                var tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }
    }
}
