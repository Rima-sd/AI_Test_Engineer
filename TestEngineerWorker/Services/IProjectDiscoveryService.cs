
namespace TestEngineerWorker.Services;

public interface IProjectDiscoveryService
{
    ProjectInventory Discover(string workspaceRoot);
}
