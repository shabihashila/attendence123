// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using System.Text.Json.Serialization;

namespace FegStorage.WebApi.Storage;

// Wire enum. String-serialized so request payloads read clearly in
// Swagger + log lines, and a typo in JSON surfaces as a 400 instead
// of silently picking Local (= 0). The four values match the v1
// roadmap; future providers append.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StorageProvider
{
    Local      = 0,
    S3         = 1,
    R2         = 2,   // Cloudflare R2 -- S3-compatible, served by S3StorageBackend with an endpoint override.
    AzureBlob  = 3
}
