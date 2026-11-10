
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();


await app.BootUmbracoAsync();



app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

bool createSample = builder.Configuration.GetValue<bool>("MemberCacheDemo:CreateSample");
bool verifyAndExit = builder.Configuration.GetValue<bool>("MemberCacheDemo:VerifyAndExit");
if (createSample || verifyAndExit)
{
    await app.StartAsync();
    if (createSample) await DemoFixture.RunAsync(app.Services, builder.Configuration);
    if (verifyAndExit) await app.StopAsync();
    else await app.WaitForShutdownAsync();
}
else
{
    await app.RunAsync();
}
