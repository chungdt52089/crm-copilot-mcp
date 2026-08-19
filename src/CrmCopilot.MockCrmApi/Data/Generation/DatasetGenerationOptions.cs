namespace CrmCopilot.MockCrmApi.Data.Generation;

/// <summary>
/// Parameters controlling <see cref="SyntheticDatasetGenerator"/>. Same seed + same
/// CustomerCount always produce byte-identical output — no wall-clock time, GUIDs, or
/// unordered collection iteration are used anywhere in generation.
/// </summary>
internal sealed record DatasetGenerationOptions(int Seed, int CustomerCount)
{
    public const int DefaultSeed = 20260818;
    public const int DefaultCustomerCount = 12;

    public static readonly DatasetGenerationOptions Default = new(DefaultSeed, DefaultCustomerCount);
}
