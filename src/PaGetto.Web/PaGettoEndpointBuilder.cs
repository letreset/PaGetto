using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;

namespace PaGetto.Web;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Would be a breaking change since it's part of the public API")]
public class PaGettoEndpointBuilder
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        endpoints.MapControllers();

        MapServiceIndexRoutes(endpoints);
        MapPackagePublishRoutes(endpoints);
        MapSymbolRoutes(endpoints);
        MapSearchRoutes(endpoints);
        MapPackageMetadataRoutes(endpoints);
        MapPackageContentRoutes(endpoints);
        MapPubRoutes(endpoints);
        MapNpmRoutes(endpoints);
    }

    public void MapServiceIndexRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute(
            name: Routes.IndexRouteName,
            pattern: "v3/index.json",
            defaults: new { controller = "ServiceIndex", action = "Get" });
    }

    public void MapPackagePublishRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute(
            name: Routes.UploadPackageRouteName,
            pattern: "api/v2/package",
            defaults: new { controller = "PackagePublish", action = "Upload" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

        endpoints.MapControllerRoute(
            name: Routes.DeleteRouteName,
            pattern: "api/v2/package/{id}/{version}",
            defaults: new { controller = "PackagePublish", action = "Delete" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("DELETE") });

        endpoints.MapControllerRoute(
            name: Routes.RelistRouteName,
            pattern: "api/v2/package/{id}/{version}",
            defaults: new { controller = "PackagePublish", action = "Relist" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("POST") });
    }

    public void MapSymbolRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute(
            name: Routes.UploadSymbolRouteName,
            pattern: "api/v2/symbol",
            defaults: new { controller = "Symbol", action = "Upload" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("PUT") });

        endpoints.MapControllerRoute(
            name: Routes.SymbolDownloadRouteName,
            pattern: "api/download/symbols/{file}/{key}/{file2}",
            defaults: new { controller = "Symbol", action = "Get" });

        endpoints.MapControllerRoute(
            name: Routes.PrefixedSymbolDownloadRouteName,
            pattern: "api/download/symbols/{prefix}/{file}/{key}/{file2}",
            defaults: new { controller = "Symbol", action = "Get" });
    }

    public void MapSearchRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute(
            name: Routes.SearchRouteName,
            pattern: "v3/search",
            defaults: new { controller = "Search", action = "Search" });

        endpoints.MapControllerRoute(
            name: Routes.AutocompleteRouteName,
            pattern: "v3/autocomplete",
            defaults: new { controller = "Search", action = "Autocomplete" });

        // This is an unofficial API to find packages that depend on a given package.
        endpoints.MapControllerRoute(
            name: Routes.DependentsRouteName,
            pattern: "v3/dependents",
            defaults: new { controller = "Search", action = "Dependents" });
    }

    public void MapPackageMetadataRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute(
           name: Routes.RegistrationIndexRouteName,
           pattern: "v3/registration/{id}/index.json",
           defaults: new { controller = "PackageMetadata", action = "RegistrationIndex" });

        endpoints.MapControllerRoute(
            name: Routes.RegistrationPageRouteName,
            pattern: "v3/registration/{id}/page/{lower}/{upper}.json",
            defaults: new { controller = "PackageMetadata", action = "RegistrationPage" });

        endpoints.MapControllerRoute(
            name: Routes.RegistrationLeafRouteName,
            pattern: "v3/registration/{id}/{version}.json",
            defaults: new { controller = "PackageMetadata", action = "RegistrationLeaf" });
    }

    /// <summary>
    /// The pub API of Dart feeds.
    /// See: https://github.com/dart-lang/pub/blob/master/doc/repository-spec-v2.md
    /// </summary>
    public void MapPubRoutes(IEndpointRouteBuilder endpoints)
    {
        var get = new { httpMethod = new HttpMethodRouteConstraint("GET") };

        endpoints.MapControllerRoute(
            name: Routes.PubNewVersionRouteName,
            pattern: "api/packages/versions/new",
            defaults: new { controller = "PubPackage", action = "NewVersion" },
            constraints: get);

        endpoints.MapControllerRoute(
            name: Routes.PubUploadRouteName,
            pattern: "api/packages/versions/newUpload",
            defaults: new { controller = "PubPackage", action = "Upload" },
            constraints: new { httpMethod = new HttpMethodRouteConstraint("POST") });

        endpoints.MapControllerRoute(
            name: Routes.PubFinishUploadRouteName,
            pattern: "api/packages/versions/newUploadFinish",
            defaults: new { controller = "PubPackage", action = "FinishUpload" },
            constraints: get);

        endpoints.MapControllerRoute(
            name: Routes.PubPackageRouteName,
            pattern: "api/packages/{name}",
            defaults: new { controller = "PubPackage", action = "GetPackage" },
            constraints: get);

        endpoints.MapControllerRoute(
            name: Routes.PubPackageVersionRouteName,
            pattern: "api/packages/{name}/versions/{version}",
            defaults: new { controller = "PubPackage", action = "GetPackageVersion" },
            constraints: get);

        endpoints.MapControllerRoute(
            name: Routes.PubArchiveRouteName,
            pattern: "packages/{name}/versions/{version}.tar.gz",
            defaults: new { controller = "PubPackage", action = "DownloadArchive" },
            constraints: get);
    }

    /// <summary>
    /// The npm registry API of npm feeds. The registry is at <c>npm/</c> under the feed, so that
    /// package names can't collide with the pages of the web UI (there are npm packages called
    /// "connect" and "statistics").
    /// See: https://github.com/npm/registry/blob/main/docs/REGISTRY-API.md
    /// </summary>
    public void MapNpmRoutes(IEndpointRouteBuilder endpoints)
    {
        var get = new { httpMethod = new HttpMethodRouteConstraint("GET") };
        var put = new { httpMethod = new HttpMethodRouteConstraint("PUT") };
        var delete = new { httpMethod = new HttpMethodRouteConstraint("DELETE") };

        endpoints.MapControllerRoute(
            name: null,
            pattern: "npm/-/ping",
            defaults: new { controller = "NpmPackage", action = "Ping" },
            constraints: get);

        endpoints.MapControllerRoute(
            name: null,
            pattern: "npm/-/whoami",
            defaults: new { controller = "NpmPackage", action = "WhoAmI" },
            constraints: get);

        endpoints.MapControllerRoute(
            name: null,
            pattern: "npm/-/v1/search",
            defaults: new { controller = "NpmPackage", action = "Search" },
            constraints: get);

        // A scoped name is sent as one segment here: @scope%2fname.
        endpoints.MapControllerRoute(
            name: null,
            pattern: "npm/-/package/{name}/dist-tags",
            defaults: new { controller = "NpmPackage", action = "GetDistTags" },
            constraints: get);

        endpoints.MapControllerRoute(
            name: null,
            pattern: "npm/-/package/{name}/dist-tags/{tag}",
            defaults: new { controller = "NpmPackage", action = "SetDistTag" },
            constraints: put);

        endpoints.MapControllerRoute(
            name: null,
            pattern: "npm/-/package/{name}/dist-tags/{tag}",
            defaults: new { controller = "NpmPackage", action = "RemoveDistTag" },
            constraints: delete);

        // Clients send a scoped name as @scope%2fname, which matches {name}; @scope/name matches
        // the scoped routes. Tarball URLs are in the scoped form, like the npm registry's.
        foreach (var prefix in new[] { "npm/{name}", "npm/@{scope}/{name}" })
        {
            endpoints.MapControllerRoute(
                name: null,
                pattern: prefix,
                defaults: new { controller = "NpmPackage", action = "GetPackage" },
                constraints: get);

            endpoints.MapControllerRoute(
                name: null,
                pattern: prefix + "/-/{fileName}",
                defaults: new { controller = "NpmPackage", action = "DownloadTarball" },
                constraints: get);

            // npm publish and npm deprecate send the package document to the first; npm unpublish
            // of one version sends it to the second.
            endpoints.MapControllerRoute(
                name: null,
                pattern: prefix,
                defaults: new { controller = "NpmPackage", action = "PutPackage" },
                constraints: put);

            endpoints.MapControllerRoute(
                name: null,
                pattern: prefix + "/-rev/{rev}",
                defaults: new { controller = "NpmPackage", action = "PutPackage" },
                constraints: put);

            // npm unpublish of a whole package.
            endpoints.MapControllerRoute(
                name: null,
                pattern: prefix + "/-rev/{rev}",
                defaults: new { controller = "NpmPackage", action = "UnpublishPackage" },
                constraints: delete);
        }

        // The last step of npm unpublish of one version deletes the tarball. The client builds the
        // URL from the tarball URL's path, which repeats the feed's path below a registry that is
        // not at the root of its host, so any path is accepted.
        endpoints.MapControllerRoute(
            name: null,
            pattern: "npm/{**path}",
            defaults: new { controller = "NpmPackage", action = "DeleteTarball" },
            constraints: delete);
    }

    public void MapPackageContentRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute(
            name: Routes.PackageVersionsRouteName,
            pattern: "v3/package/{id}/index.json",
            defaults: new { controller = "PackageContent", action = "GetPackageVersions" });

        endpoints.MapControllerRoute(
            name: Routes.PackageDownloadRouteName,
            pattern: "v3/package/{id}/{version}/{idVersion}.nupkg",
            defaults: new { controller = "PackageContent", action = "DownloadPackage" });

        endpoints.MapControllerRoute(
            name: Routes.PackageDownloadManifestRouteName,
            pattern: "v3/package/{id}/{version}/{id2}.nuspec",
            defaults: new { controller = "PackageContent", action = "DownloadNuspec" });

        endpoints.MapControllerRoute(
            name: Routes.PackageDownloadReadmeRouteName,
            pattern: "v3/package/{id}/{version}/readme",
            defaults: new { controller = "PackageContent", action = "DownloadReadme" });

        endpoints.MapControllerRoute(
            name: Routes.PackageDownloadIconRouteName,
            pattern: "v3/package/{id}/{version}/icon",
            defaults: new { controller = "PackageContent", action = "DownloadIcon" });
    }
}
