namespace BuildingMaterial.Api;

public sealed class RecaptchaValidator(IConfiguration configuration, IHttpClientFactory httpClientFactory)
{
    public async Task EnsureValidAsync(string? token, CancellationToken cancellationToken)
    {
        if (!string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
            return;
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("reCAPTCHA token is required.");
        var secret = configuration["Recaptcha:SecretKey"] ?? throw new InvalidOperationException("Recaptcha:SecretKey is not configured.");
        using var response = await httpClientFactory.CreateClient().PostAsync(
            $"https://www.google.com/recaptcha/api/siteverify?secret={Uri.EscapeDataString(secret)}&response={Uri.EscapeDataString(token)}",
            null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RecaptchaResponse>(cancellationToken);
        if (result is not { Success: true }) throw new InvalidOperationException("reCAPTCHA validation failed.");
    }

    private sealed record RecaptchaResponse(bool Success);
}
