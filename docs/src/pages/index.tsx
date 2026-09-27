import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Heading from '@theme/Heading';
import Layout from '@theme/Layout';
import HomepageFeatures from '@site/src/components/HomepageFeatures';
import QuickStartTerminal from '@site/src/components/QuickStartTerminal';

import styles from './index.module.css';

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={`hero ${styles.hero}`}>
      <div className={styles.heroInner}>
        <div className={styles.heroText}>
          <div className={styles.brand}>
            <img className={styles.logo} src={useBaseUrl('/img/logo.svg')} alt="" />
            <Heading as="h1" className={`hero__title ${styles.title}`}>
              {siteConfig.title}
            </Heading>
          </div>
          <p className={`hero__subtitle ${styles.tagline}`}>{siteConfig.tagline}</p>
          <p className={styles.subtitle}>
            Multiple feeds, per-feed permissions, Entra ID sign-in and read-through mirrors
            of nuget.org. Runs on Docker, Kubernetes or any machine with .NET.
          </p>
          <div className={styles.buttons}>
            <Link className="button button--primary" to="/docs">
              Get started
            </Link>
            <Link className="button button--secondary" href="https://github.com/letreset/PaGetto">
              GitHub
            </Link>
          </div>
        </div>
        <QuickStartTerminal />
      </div>
    </header>
  );
}

export default function Home(): JSX.Element {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout title={siteConfig.tagline} description="PaGetto is a lightweight, self-hosted NuGet and symbol server with multiple feeds, per-feed permissions and Entra ID sign-in.">
      <HomepageHeader />
      <main>
        <HomepageFeatures />
      </main>
    </Layout>
  );
}
