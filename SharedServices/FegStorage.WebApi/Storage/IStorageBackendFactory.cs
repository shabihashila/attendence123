// Author: Md. Sakibur Rahman
// Date:   2026-05-13
namespace FegStorage.WebApi.Storage;

// Resolves the BackendConfig in the inbound request to a fresh
// IStorageBackend instance. Stateless -- nothing about the tenant is
// cached. The factory is the only place that knows the full set of
// supported providers; controllers depend only on this interface.
public interface IStorageBackendFactory
{
    IStorageBackend Create(BackendConfig config);
}
