# Maven packages

PaGetto can host Java, Kotlin and Scala libraries next to NuGet, Dart and npm packages. A feed of the type **Maven** is a Maven repository: Maven, Gradle and sbt download from it and deploy to it, and it can mirror [Maven Central](https://repo.maven.apache.org/maven2) or another Maven repository.

A feed serves one kind of package. Its type is chosen when the feed is created and can't be changed afterwards.

## Create a Maven feed

On **Admin > Feeds**, press **New feed** and choose the type **Maven** (see [Managing feeds](feeds.md#managing-feeds)). The feed's **repository URL** is its address followed by `/maven2/`:

```
https://your-server/feeds/jvm/maven2/
```

The repository layout is nothing but paths, so it lives below `/maven2/`, clear of the pages of the web UI. The feed's **Connect** page shows the repository URL with the settings below filled in.

## Configure the client

### Maven

Maven reads the credentials from `~/.m2/settings.xml`, by the id of the repository. Add the feed as a repository, here in a profile that is always active:

```xml
<settings>
  <servers>
    <server>
      <id>jvm</id>
      <username>USERNAME</username>
      <password>YOUR_TOKEN</password>
    </server>
  </servers>
  <profiles>
    <profile>
      <id>jvm</id>
      <repositories>
        <repository>
          <id>jvm</id>
          <url>https://your-server/feeds/jvm/maven2/</url>
          <snapshots><enabled>true</enabled></snapshots>
        </repository>
      </repositories>
    </profile>
  </profiles>
  <activeProfiles>
    <activeProfile>jvm</activeProfile>
  </activeProfiles>
</settings>
```

When the feed mirrors Maven Central (see below), send every download through it instead, plugins included:

```xml
<mirrors>
  <mirror>
    <id>jvm</id>
    <mirrorOf>*</mirrorOf>
    <url>https://your-server/feeds/jvm/maven2/</url>
  </mirror>
</mirrors>
```

Maven 3.8.1 and later refuse plain HTTP repositories other than `localhost`.

### Gradle

```kotlin
// settings.gradle.kts
dependencyResolutionManagement {
    repositories {
        maven {
            name = "pagetto"
            url = uri("https://your-server/feeds/jvm/maven2/")
            credentials(PasswordCredentials::class)
        }
    }
}
```

`credentials(PasswordCredentials::class)` reads the Gradle properties `pagettoUsername` and `pagettoPassword`, for example from `~/.gradle/gradle.properties`. A plain HTTP repository needs `isAllowInsecureProtocol = true`.

### sbt

```scala
resolvers += "jvm" at "https://your-server/feeds/jvm/maven2/"
credentials += Credentials("PaGetto", "your-server", "USERNAME", "YOUR_TOKEN")
```

sbt matches credentials by the realm of the server's challenge, which is `PaGetto` for Maven feeds.

### Authentication

With `Authentication:Mode` set to `Local`, `Entra` or `Hybrid`, the password is a [personal access token](authentication.md#personal-access-tokens-pats) (a local account's own password works too). The clients send it with Basic authentication once the server asks for it. The account needs the **pull** permission on the feed to download, and **push** to deploy (see [Feed permissions](authentication.md#feed-permissions)).

With the `Legacy` mode, a feed without configured credentials needs no password to download. Deploying needs one of the configured API keys as the password; the username is not checked.

## Deploy

With Maven, point `distributionManagement` at the feed, using the id of the `settings.xml` server:

```xml
<distributionManagement>
  <repository>
    <id>jvm</id>
    <url>https://your-server/feeds/jvm/maven2/</url>
  </repository>
  <snapshotRepository>
    <id>jvm</id>
    <url>https://your-server/feeds/jvm/maven2/</url>
  </snapshotRepository>
</distributionManagement>
```

and run `mvn deploy`. With Gradle, add the feed to the repositories of the `maven-publish` plugin and run `./gradlew publish`:

```kotlin
publishing {
    publications { create<MavenPublication>("maven") { from(components["java"]) } }
    repositories {
        maven {
            name = "pagetto"
            url = uri("https://your-server/feeds/jvm/maven2/")
            credentials(PasswordCredentials::class)
        }
    }
}
```

A package is named `groupId:artifactId`, and each of its versions holds the files deployed to it: the POM, the JAR, sources, javadoc, Gradle module metadata, signatures. PaGetto checks each upload:

- The path must be a file of the [repository layout](https://maven.apache.org/repositories/layout.html): `{group path}/{artifactId}/{version}/{artifactId}-{version}[-{classifier}].{extension}`, with letters, digits, `.`, `-`, `_` and `+`. The package name is at most 128 characters, the version at most 128 and the file name at most 256.
- A POM must be XML, and its group id, artifact id and version must be those of its path. A value that is still a property, such as the `${revision}` of a CI-friendly version deployed without the flatten plugin, is not checked.
- A file must not be larger than the feed's **Max package size**, which applies to each file.
- A file that the version already has is rejected with `409 Conflict`, unless the feed's **Package overwrite policy** allows replacing it. Uploading the same content again succeeds, since clients retry. The files of `SNAPSHOT` versions can always be replaced.
- A read-only feed refuses every upload with `403 Forbidden`.

The server computes the checksums (`.sha1`, `.md5`, `.sha256`, `.sha512`) of every file and writes the `maven-metadata.xml` files itself. The checksums the clients upload are compared with the stored file and rejected when they differ; the metadata they upload is ignored. Maven and Gradle show the reason of a rejected upload, for example `already exists in com.example:my-lib 1.0, and the feed does not allow overwriting it`.

### Snapshots

A `SNAPSHOT` version is deployed with a timestamped file per build, such as `my-lib-1.0-20240102.030405-3.jar`. Its `maven-metadata.xml` points clients at the newest build of each file. All builds are kept until the version is deleted.

## Mirror Maven Central

A Maven feed can mirror other Maven repositories. On the feed's settings page, add a mirror with the URL `https://repo.maven.apache.org/maven2` (see [Mirror](feeds.md#mirror-read-through-cache) for the settings every mirror has). Then:

- An artifact's `maven-metadata.xml` combines the versions published to the feed with those of the mirror.
- A file of a release version is downloaded from the mirror on its first use, checked against the `.sha1` the mirror lists, stored in the feed and served from there afterwards.
- A version published to the feed hides the same version of the mirror: a file it doesn't have is not fetched from the mirror.
- `SNAPSHOT` versions are not mirrored.
- With several mirrors, they are asked in order and the first one that has a file serves it. A mirror that is unreachable is skipped.
- The `maven-metadata.xml` files of mirrors, which also tell Maven the prefixes of plugins (`mvn dependency:tree`), are cached in memory like the version lists of NuGet mirrors (see [Upstream listing cache](feeds.md#upstream-listing-cache)).

## In the web UI

A Maven feed lists the packages stored in it, with search. A package's page shows the POM's description, dependencies, license and links, the files of the version, the versions, and the dependency snippets for Maven, Gradle and sbt. Packages that are only available from a mirror appear once a file has been downloaded.

Users with the **delete** permission can **unlist** a version, which leaves it out of `maven-metadata.xml` (it can still be downloaded by its exact version), **relist** it, or delete it with its files for good.

## What is different from NuGet feeds

| | Maven feeds |
|---|---|
| Retention | Not available. Old versions and snapshot builds are only removed by hand |
| Deletion behavior | Not available: there is no delete in the repository layout |
| Upload from the browser | Not available. Deploy with Maven or Gradle |
| Group metadata | The plugin prefixes of a group come from the mirrors only, not from plugins deployed to the feed |
| Search | Only the web UI searches; the repository layout has no search |

The NuGet, pub and npm APIs answer `404 Not Found` on a Maven feed, and the Maven repository answers `404 Not Found` on other feeds.
