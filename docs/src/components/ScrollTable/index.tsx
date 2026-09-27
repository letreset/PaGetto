import {useEffect, useRef, useState, type ComponentProps, type ReactNode} from 'react';
import clsx from 'clsx';

// A markdown table that scrolls sideways when it is wider than the page. While more columns
// are hidden on the right, the wrapper shows a shadow on that edge. On a phone the CSS turns
// every row into a card instead; each cell gets its column header as data-label for that.
export default function ScrollTable(props: ComponentProps<'table'>): ReactNode {
  const ref = useRef<HTMLTableElement>(null);
  const [hiddenRight, setHiddenRight] = useState(false);
  const [labelled, setLabelled] = useState(false);

  useEffect(() => {
    const table = ref.current;
    if (!table) return undefined;

    const headers = Array.from(table.querySelectorAll('thead th'), (th) => th.textContent?.trim() ?? '');
    table.querySelectorAll('tbody tr').forEach((row) => {
      Array.from(row.children).forEach((cell, i) => cell.setAttribute('data-label', headers[i] ?? ''));
    });
    setLabelled(headers.length > 0);

    const update = () => setHiddenRight(table.scrollWidth - table.clientWidth - table.scrollLeft > 1);
    update();
    table.addEventListener('scroll', update, {passive: true});
    const observer = new ResizeObserver(update);
    observer.observe(table);
    return () => {
      table.removeEventListener('scroll', update);
      observer.disconnect();
    };
  }, []);

  return (
    <div className={clsx('bgt-table', hiddenRight && 'bgt-table--more', labelled && 'bgt-table--stack')}>
      <table ref={ref} {...props} />
    </div>
  );
}
