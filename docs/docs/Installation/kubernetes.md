# Run PaGetto on Kubernetes

Every release publishes a Helm chart as an OCI artifact on GitHub Container Registry. The chart is built on the [bjw-s common library](https://github.com/bjw-s-labs/helm-charts/tree/main/charts/library/common), so every option of that library is available.

## Install

```shell
helm install pagetto oci://ghcr.io/letreset/charts/pagetto --version 1.0.0
```

The chart version always matches the PaGetto version, and the chart deploys the image `letreset/pagetto` with that same tag. See the [releases page](https://github.com/letreset/PaGetto/releases) for the available versions.

To see every value with its default:

```shell
helm show values oci://ghcr.io/letreset/charts/pagetto --version 1.0.0
```

## Configuration

PaGetto's settings are environment variables in a ConfigMap, under `configMaps.pagetto-env.data`. The defaults use SQLite and file system storage on the persistent volume:

```yaml
configMaps:
  pagetto-env:
    data:
      Authentication__Mode: "Local"
      Storage__Type: "FileSystem"
      Storage__Path: "/data"
      Database__Type: "Sqlite"
      Database__ConnectionString: "Data Source=/data/pagetto.db"
```

:::warning

**Change `ApiKey`.** The default is `ChangeMe`. Better, remove it from the ConfigMap and pass it from a Secret, as shown below.

:::

Any setting from [Configuration](../configuration.md) can be added the same way, with `__` as the section separator (for example `Authentication__Mode: "Local"`).

### Secrets

Keep secrets such as the API key, database passwords and the Entra client secret in a Kubernetes Secret, and add it to the container's `envFrom`:

```shell
kubectl create secret generic pagetto-secrets \
  --from-literal=ApiKey='a-long-random-value' \
  --from-literal=Database__ConnectionString='Host=postgres;Database=pagetto;Username=pagetto;Password=...'
```

```yaml
controllers:
  pagetto:
    containers:
      pagetto:
        envFrom:
          - configMapRef:
              name: pagetto-pagetto-env
          - secretRef:
              name: pagetto-secrets
```

`envFrom` replaces the default list, so keep the ConfigMap entry. Its name is `<release name>-pagetto-env`, so adjust it if your release isn't called `pagetto`. When a key is in both, the Secret wins because it is listed last.

## Key values

| Key | Default | Description |
|-----|---------|-------------|
| `configMaps.pagetto-env.data` | SQLite and file system on `/data` | PaGetto settings as environment variables |
| `controllers.pagetto.containers.pagetto.image.tag` | The chart version | Image tag |
| `controllers.pagetto.containers.pagetto.resources` | 100m / 256Mi requested, 500m / 512Mi limit | CPU and memory |
| `controllers.pagetto.containers.pagetto.probes` | `/livez` liveness, `/health` readiness | Health probes |
| `service.pagetto-srv.ports.http.port` | `8080` | Service port |
| `ingress.pagetto-ingress.enabled` | `false` | Create an Ingress |
| `persistence.pagetto-data` | 10Gi, `ReadWriteOnce`, mounted at `/data` | Packages, symbols, the SQLite database and Data Protection keys |

## Probes

The liveness probe calls `/livez`, which only checks that the process responds. The readiness probe calls `/health`, which also checks the database (but not the package storage), so a pod that can't reach the database is taken out of the Service without being restarted. If you change `HealthCheck__Path`, change the readiness probe path too.

## Ingress

```yaml
ingress:
  pagetto-ingress:
    enabled: true
    className: nginx
    annotations:
      # Allow large packages (the default package limit is 8 GiB).
      nginx.ingress.kubernetes.io/proxy-body-size: "0"
    hosts:
      - host: nuget.example.com
        paths:
          - path: /
            service:
              identifier: pagetto-srv
              port: http
    tls:
      - secretName: nuget-example-com-tls
        hosts:
          - nuget.example.com
```

The ingress controller must forward `X-Forwarded-Proto` and `X-Forwarded-Host` (ingress-nginx does by default), so PaGetto returns `https://` URLs in its service index.

## Scaling out

With the defaults (SQLite and a `ReadWriteOnce` volume) run **one replica**. To run more:

- Use PostgreSQL, SQL Server or MySQL instead of SQLite.
- Use shared storage: a `ReadWriteMany` volume, or cloud storage such as Azure Blob Storage or AWS S3.

The Data Protection keys live in the package storage, so every replica shares them and sign-in cookies work on all of them.

## Upgrade

```shell
helm upgrade pagetto oci://ghcr.io/letreset/charts/pagetto --version <new-version> --reuse-values
```

PaGetto runs database migrations on startup. Back up the database before upgrading across minor or major versions.
