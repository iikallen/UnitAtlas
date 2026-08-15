namespace UnitAtlas.Application.Integrations;

public interface IIntegrationAdapter
{
    string System { get; }
    Task SendAsync(string messageType, string payloadJson, CancellationToken cancellationToken);
}
