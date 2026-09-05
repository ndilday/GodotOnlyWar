using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OnlyWar.Models.Events
{
    public sealed class CampaignIdentity
    {
        public Guid CampaignId { get; }
        public long CampaignSeed { get; }
        public int RandomAlgorithmVersion { get; }

        public static CampaignIdentity Empty { get; } = new(Guid.Empty, 0, 1);

        public CampaignIdentity(Guid campaignId, long campaignSeed, int randomAlgorithmVersion = 1)
        {
            if (randomAlgorithmVersion <= 0) throw new ArgumentOutOfRangeException(nameof(randomAlgorithmVersion));
            CampaignId = campaignId;
            CampaignSeed = campaignSeed;
            RandomAlgorithmVersion = randomAlgorithmVersion;
        }

        public static CampaignIdentity CreateNew(long campaignSeed) =>
            new(Guid.NewGuid(), campaignSeed, 1);

    }

    public static class NarrativeVariantSelector
    {
        public static int SelectVariant(
            CampaignIdentity identity,
            long campaignEventId,
            string rendererKey,
            int rendererVersion,
            int variantCount)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (campaignEventId <= 0) throw new ArgumentOutOfRangeException(nameof(campaignEventId));
            if (string.IsNullOrWhiteSpace(rendererKey)) throw new ArgumentException("Renderer key is required.");
            if (rendererVersion <= 0) throw new ArgumentOutOfRangeException(nameof(rendererVersion));
            if (variantCount <= 0) throw new ArgumentOutOfRangeException(nameof(variantCount));

            byte[] bytes = StableDerivation.BuildBytes(
                identity,
                turn: 0,
                $"narrative/{campaignEventId}/{rendererKey}",
                rendererVersion);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(bytes).AsSpan(0, 4));
            return (int)(value % (uint)variantCount);
        }
    }

    public static class StableDerivation
    {
        public static byte[] BuildBytes(
            CampaignIdentity identity,
            int turn,
            string streamKey,
            int streamVersion)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (string.IsNullOrWhiteSpace(streamKey)) throw new ArgumentException("A stream key is required.");
            string canonical = FormattableString.Invariant(
                $"onlywar-rng/{identity.RandomAlgorithmVersion}/{identity.CampaignId:D}/{identity.CampaignSeed}/{turn}/{streamKey}/{streamVersion}");
            return Encoding.UTF8.GetBytes(canonical);
        }

    }
}
