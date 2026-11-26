namespace Reqviora.Infrastructure.Services;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "REQVIORA";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 3;
    public int BaseRetryMilliseconds { get; set; } = 250;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string Provider { get; set; } = "Local";
    public string RootPath { get; set; } = "./private-artifacts";
    public long MaxFileBytes { get; set; } = 25 * 1024 * 1024;
    public int RetentionDays { get; set; } = 365;
    public string ClamAvHost { get; set; } = "localhost";
    public int ClamAvPort { get; set; } = 3310;
    public bool RequireAntivirus { get; set; }
    public int AntivirusMaxAttempts { get; set; } = 2;
    public int AntivirusRetryDelayMilliseconds { get; set; } = 250;
    public int AntivirusTimeoutSeconds { get; set; } = 10;
    public string SupabaseUrl { get; set; } = "";
    public string SupabaseBucket { get; set; } = "";
    public string SupabaseServiceKey { get; set; } = "";
    public int SignedUrlSeconds { get; set; } = 300;
}

public sealed class AiOptions
{
    public const string SectionName = "AI";
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "OpenAICompatible";
    public string Endpoint { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-5-mini";
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class AiJobOptions
{
    public const string SectionName = "AIJobs";
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public int LeaseSeconds { get; set; } = 180;
    public int MaxAttempts { get; set; } = 3;
    public int BaseRetrySeconds { get; set; } = 2;
    public int MaxRetrySeconds { get; set; } = 60;
    public int MaxPromptCharacters { get; set; } = 100_000;
    public int MaxResultCharacters { get; set; } = 2_000_000;
}

public sealed class OcrOptions
{
    public const string SectionName = "OCR";
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Provider { get; set; } = "HttpGateway";
    public string Model { get; set; } = "document-ocr";
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 3;
    public int BaseRetryMilliseconds { get; set; } = 250;
}

public sealed class ProjectInvitationOptions
{
    public const string SectionName = "Invitations";
    public int ExpirationHours { get; set; } = 72;
}
