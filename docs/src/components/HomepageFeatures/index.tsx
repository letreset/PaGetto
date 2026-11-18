import Link from '@docusaurus/Link';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

type FeatureItem = {
  icon: string;
  title: string;
  to: string;
  description: string;
};

// Line icons (24x24 viewBox): grid, layers, users, refresh, database, box.
const FeatureList: FeatureItem[] = [
  {
    icon: 'M4 4h6v6H4ZM14 4h6v6h-6ZM4 14h6v6H4ZM14 14h6v6h-6Z',
    title: 'Fifteen package formats',
    to: '/docs',
    description: 'NuGet with symbols, npm, Maven, Python, Rust, Go, Ruby, Dart, Hex, PHP, CocoaPods, Swift, R, sbt and Visual Studio extensions, each speaking its own protocol.',
  },
  {
    icon: 'M12 3 3 7.5 12 12l9-4.5L12 3ZM3 12l9 4.5 9-4.5M3 16.5 12 21l9-4.5',
    title: 'Multiple feeds',
    to: '/docs/feeds',
    description: 'Separate feeds on one server, one ecosystem each, with their own packages, overwrite rules, retention, size limit and webhook.',
  },
  {
    icon: 'M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM2 21a7 7 0 0 1 14 0M16 3.5a4 4 0 0 1 0 7.5M22 21a6 6 0 0 0-4-5.6',
    title: 'Users and permissions',
    to: '/docs/authentication',
    description: 'Local accounts, Microsoft Entra ID sign-in, groups, pull, push and delete permissions per feed, and personal access tokens for CI.',
  },
  {
    icon: 'M4 12a8 8 0 0 1 14-5.3L20 9M20 4v5h-5M20 12a8 8 0 0 1-14 5.3L4 15M4 20v-5h5',
    title: 'Read-through mirrors',
    to: '/docs/feeds#mirror-read-through-cache',
    description: 'Cache nuget.org, npmjs, Maven Central, PyPI, crates.io and the other public registries per feed, several upstreams in order.',
  },
  {
    icon: 'M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3ZM4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3',
    title: 'Pluggable backends',
    to: '/docs/configuration#database-configuration',
    description: 'SQLite, SQL Server, PostgreSQL or MySQL, and the file system, Azure Blob, AWS S3, Google Cloud, Aliyun or Tencent storage.',
  },
  {
    icon: 'M21 8 12 3 3 8v8l9 5 9-5V8ZM3 8l9 5 9-5M12 13v8',
    title: 'Easy to deploy',
    to: '/docs/Installation/docker',
    description: 'Multi-arch Docker image, a Helm chart on GHCR, or a zip that runs anywhere .NET runs, including behind IIS.',
  },
];

function Feature({icon, title, to, description}: FeatureItem) {
  return (
    <Link className={styles.card} to={to}>
      <svg className={styles.watermark} viewBox="0 0 24 24" aria-hidden="true">
        <path d={icon} />
      </svg>
      <Heading as="h3" className={styles.title}>{title}</Heading>
      <p className={styles.description}>{description}</p>
    </Link>
  );
}

export default function HomepageFeatures(): JSX.Element {
  return (
    <section className={styles.features}>
      <div className={styles.grid}>
        {FeatureList.map((props) => (
          <Feature key={props.title} {...props} />
        ))}
      </div>
    </section>
  );
}
