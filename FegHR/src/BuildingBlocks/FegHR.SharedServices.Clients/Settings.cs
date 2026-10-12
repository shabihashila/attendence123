namespace FegHR.SharedServices.Clients;

/// <summary>
/// <c>FileStorageService</c> configuration section. One platform credential
/// (X-Api-Key) plus the storage target the service writes to. The target is
/// sent inline on each call, because the file service holds no configuration of its own.
/// </summary>
public sealed class FileStorageServiceSettings
{
    public const string SectionName = "FileStorageService";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public StorageTarget Storage { get; set; } = new();
}

/// <summary>Mirrors the service's config payload: provider, container and provider-specific credentials.</summary>
public sealed class StorageTarget
{
    /// <summary><c>Local</c>, <c>S3</c> or <c>Azure</c>.</summary>
    public string Provider { get; set; } = "Local";
    public string Container { get; set; } = "feghr";
    public LocalStorage? Local { get; set; }
    public S3Storage? S3 { get; set; }
    public AzureStorage? Azure { get; set; }
}

public sealed class LocalStorage
{
    public string? BasePathOverride { get; set; }
}

public sealed class S3Storage
{
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Endpoint { get; set; }
    public bool ForcePathStyle { get; set; }
}

public sealed class AzureStorage
{
    public string? ConnectionString { get; set; }
    public string? AccountName { get; set; }
    public string? AccountKey { get; set; }
}

/// <summary><c>CommunicationService</c> configuration section (email and SMS).</summary>
public sealed class CommunicationServiceSettings
{
    public const string SectionName = "CommunicationService";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public string? SmsSenderId { get; set; }
}
