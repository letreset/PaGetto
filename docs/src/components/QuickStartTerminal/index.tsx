import type {ReactNode} from 'react';
import styles from './styles.module.css';

function Prompt(): ReactNode {
  return (
    <>
      <span className={styles.home}>~</span> <span className={styles.dollar}>$</span>{' '}
    </>
  );
}

export default function QuickStartTerminal(): ReactNode {
  return (
    <figure className={styles.terminal} aria-label="Quick start">
      <div className={styles.titleBar} aria-hidden="true">
        <span className={styles.dot} style={{background: '#ff5f57'}} />
        <span className={styles.dot} style={{background: '#febc2e'}} />
        <span className={styles.dot} style={{background: '#28c840'}} />
        <span className={styles.title}>bash · quick start</span>
      </div>
      <pre className={styles.body}>
        <span className={styles.comment}># run the server</span>
        {'\n'}
        <Prompt />
        {'docker run -d -p 5000:8080 -v pagetto-data:/data \\\n    -e ApiKey=change-me letreset/pagetto:latest\n'}
        <span className={styles.output}>3f9c2a1e8b7d…</span>
        {'\n\n'}
        <span className={styles.comment}># push a package</span>
        {'\n'}
        <Prompt />
        {'dotnet nuget push -s http://localhost:5000/v3/index.json \\\n    -k change-me MyPackage.1.0.0.nupkg\n'}
        <span className={styles.success}>Your package was pushed.</span>
        {'\n'}
        <Prompt />
        <span className={styles.cursor} aria-hidden="true" />
      </pre>
    </figure>
  );
}
