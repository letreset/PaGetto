using PaGetto.Core.Extensions;
using PaGetto.Database.Sqlite;
using PaGetto.Web;
using PaGetto.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder();

// This will add the PaGetto services and options to the container.
builder.Services.AddPaGettoWebApplication(pagetto =>
{
    pagetto.AddSqliteDatabase();
    pagetto.AddFileStorage();
});
var app = builder.Build();

if (builder.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();

// Add PaGetto's endpoints.
new PaGettoEndpointBuilder().MapEndpoints(app);

await app.RunMigrationsAsync();
await app.RunAsync();
