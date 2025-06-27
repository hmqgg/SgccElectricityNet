namespace SgccElectricityNet.Worker.Services.Fetcher;

public record FetcherOptions(
    string? Username,
    string? Password,
    int MaxAttempts = 5);
