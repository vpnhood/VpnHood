using System.Net;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Dom;

public class ServerFarmDom : IDisposable
{
    private readonly bool _autoDisposeApp;
    public TestApp TestApp { get; }
    public ServerFarm ServerFarm { get; private set; }
    public Certificate CertificateInToken { get; private set; } = default!;
    public List<ServerDom> Servers { get; private set; } = [];
    public Guid ServerFarmId => ServerFarm.ServerFarmId;
    public DateTime CreatedTime { get; } = DateTime.UtcNow;
    public Guid ProjectId => TestApp.ProjectId;
    public ServerDom DefaultServer => Servers.First();
    public ServerFarmsClient Client => TestApp.ServerFarmsClient;

    protected ServerFarmDom(TestApp testApp, ServerFarm serverFarm, bool autoDisposeApp)
    {
        _autoDisposeApp = autoDisposeApp;
        TestApp = testApp;
        ServerFarm = serverFarm;
    }

    public static async Task<ServerFarmDom> Create(TestApp? testApp = default,
        ServerFarmCreateParams? createParams = default, int serverCount = 1)
    {
        var autoDisposeApp = testApp == null;
        testApp ??= await TestApp.Create();
        createParams ??= new ServerFarmCreateParams {
            ServerFarmName = Guid.NewGuid().ToString()
        };

        var serverFarmData = await testApp.ServerFarmsClient.CreateAsync(testApp.ProjectId, createParams);
        var ret = new ServerFarmDom(testApp, serverFarmData.ServerFarm, autoDisposeApp);
        ret.CertificateInToken = await ret.GetCertificateInToken();

        for (var i = 0; i < serverCount; i++)
            await ret.AddNewServer();

        return ret;
    }

    public static async Task<ServerFarmDom> Attach(TestApp testApp, Guid serverFarmId)
    {
        var serverFarmData = await testApp.ServerFarmsClient.GetAsync(testApp.ProjectId, serverFarmId);
        var serverFarm = new ServerFarmDom(testApp, serverFarmData.ServerFarm, false);
        await serverFarm.ReattachServers();
        return serverFarm;
    }

    public async Task<ServerFarmData> Update(ServerFarmUpdateParams updateParams)
    {
        var serverFarmData = await Client.UpdateAsync(ProjectId, ServerFarmId, updateParams);
        ServerFarm = serverFarmData.ServerFarm;
        return serverFarmData;
    }

    public async Task<ServerFarmData> Reload()
    {
        var serverFarmData = await TestApp.ServerFarmsClient.GetAsync(ProjectId, ServerFarmId, includeSummary: true);
        ServerFarm = serverFarmData.ServerFarm;
        CertificateInToken = await GetCertificateInToken();
        return serverFarmData;
    }

    public async Task ReattachServers()
    {
        var servers = await TestApp.ServersClient.ListAsync(TestApp.ProjectId, serverFarmId: ServerFarmId);
        Servers = servers.Select(serverData => ServerDom.Attach(TestApp, serverData.Server)).ToList();
    }

    public async Task ReloadServers()
    {
        foreach (var server in Servers)
            await server.Reload();
    }


    public async Task<AccessTokenDom> CreateAccessToken(bool isPublic = false)
    {
        var ret = await TestApp.AccessTokensClient.CreateAsync(TestApp.ProjectId,
            new AccessTokenCreateParams {
                ServerFarmId = ServerFarm.ServerFarmId,
                IsPublic = isPublic,
                IsEnabled = true
            });

        return new AccessTokenDom(TestApp, ret);
    }

    public async Task<AccessTokenDom> CreateAccessToken(AccessTokenCreateParams createParams)
    {
        if (createParams.ServerFarmId != Guid.Empty && createParams.ServerFarmId != ServerFarmId)
            throw new InvalidOperationException($"{nameof(ServerFarmId)} must be an empty guid or current id!");

        createParams.ServerFarmId = ServerFarm.ServerFarmId;

        var ret = await TestApp.AccessTokensClient.CreateAsync(TestApp.ProjectId, createParams);
        return new AccessTokenDom(TestApp, ret);
    }

    public async Task<ServerDom> AddNewServer(bool configure = true, bool sendStatus = true,
        IPAddress? publicIpV4 = null, int? logicalCore = null)
    {
        var sampleServer = await ServerDom.Create(TestApp, ServerFarmId, configure,
            sendStatus, publicIpV4, logicalCore: logicalCore);

        Servers.Add(sampleServer);
        return sampleServer;
    }

    public async Task<ServerDom> AddNewServer(ServerCreateParams createParams, bool configure = true,
        bool sendStatus = true, IPAddress? publicIpV4 = null)
    {
        // ReSharper disable once LocalizableElement
        if (createParams.ServerFarmId != ServerFarmId && createParams.ServerFarmId != Guid.Empty)
            throw new ArgumentException($"{nameof(createParams.ServerFarmId)} must be the same as this farm",
                nameof(createParams));

        createParams.ServerFarmId = ServerFarmId;
        var sampleServer = await ServerDom.Create(TestApp, createParams, configure, sendStatus,
            publicIpV4: publicIpV4);

        Servers.Add(sampleServer);
        return sampleServer;
    }

    public ServerDom FindServerByEndPoint(IPEndPoint ipEndPoint)
    {
        var serverDom = Servers.First(x =>
            x.Server.AccessPoints.Any(accessPoint =>
                new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort).Equals(ipEndPoint)));
        return serverDom;
    }

    public Task<Certificate> CertificateReplace(CertificateCreateParams? createParams = null)
    {
        return TestApp.ServerFarmsClient.CertificateReplaceAsync(ProjectId, ServerFarmId, createParams);
    }

    public async Task<Certificate[]> CertificateList()
    {
        var ret = await TestApp.ServerFarmsClient.CertificateListAsync(ProjectId, ServerFarmId);
        return ret.ToArray();
    }

    private async Task<Certificate> GetCertificateInToken()
    {
        var ret = await TestApp.ServerFarmsClient.CertificateListAsync(ProjectId, ServerFarmId);
        return ret.Single(x => x.IsInToken);
    }


    public Task<Certificate> CertificateImport(CertificateImportParams importParams)
    {
        return TestApp.ServerFarmsClient.CertificateImportAsync(ProjectId, ServerFarmId, importParams);
    }

    public void Dispose()
    {
        if (_autoDisposeApp)
            TestApp.Dispose();
    }
}