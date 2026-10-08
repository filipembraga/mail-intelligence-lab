import { useMemo, useState } from 'react'
import type { PlanRow } from '../api/client'

type SortKey =
  | 'senderName'
  | 'senderAddress'
  | 'messageCount'
  | 'messagesWithAttachmentsCount'
  | 'attachmentFileCount'
  | 'totalAttachmentSizeBytes'
  | 'averageAgeYears'
  | 'oldestReceivedDate'
  | 'newestReceivedDate'
  | 'action'

type Direction = 'asc' | 'desc'

type Column = {
  label: string
  sortKey: SortKey
  firstDirection: Direction
  render: (row: PlanRow) => string | number
}

const columns: Column[] = [
  { label: 'Sender', sortKey: 'senderName', firstDirection: 'asc', render: (r) => r.senderName },
  { label: 'Address', sortKey: 'senderAddress', firstDirection: 'asc', render: (r) => r.senderAddress },
  { label: 'Messages', sortKey: 'messageCount', firstDirection: 'desc', render: (r) => r.messageCount },
  { label: 'With attachments', sortKey: 'messagesWithAttachmentsCount', firstDirection: 'desc', render: (r) => r.messagesWithAttachmentsCount },
  { label: 'Files', sortKey: 'attachmentFileCount', firstDirection: 'desc', render: (r) => r.attachmentFileCount },
  // Sorted by bytes: the MB value is rounded to an integer, so many rows would tie.
  { label: 'Size (MB)', sortKey: 'totalAttachmentSizeBytes', firstDirection: 'desc', render: (r) => r.totalAttachmentSizeMB },
  { label: 'Avg age (y)', sortKey: 'averageAgeYears', firstDirection: 'desc', render: (r) => r.averageAgeYears },
  { label: 'Oldest', sortKey: 'oldestReceivedDate', firstDirection: 'asc', render: (r) => r.oldestReceivedDate },
  { label: 'Newest', sortKey: 'newestReceivedDate', firstDirection: 'asc', render: (r) => r.newestReceivedDate },
  { label: 'Action', sortKey: 'action', firstDirection: 'desc', render: (r) => r.action },
]

type Sort = { key: SortKey; direction: Direction }

function compareOrdinal(x: string, y: string): number {
  return x < y ? -1 : x > y ? 1 : 0
}

function compareBy(a: PlanRow, b: PlanRow, key: SortKey): number {
  const x = a[key]
  const y = b[key]
  if (typeof x === 'number' && typeof y === 'number') return x - y
  // Names are read by humans: accents and case should not split one person into two places.
  if (key === 'senderName') return String(x).localeCompare(String(y), undefined, { sensitivity: 'base' })
  return compareOrdinal(String(x), String(y))
}

export default function PlanTable({ rows }: { rows: PlanRow[] }) {
  const [sort, setSort] = useState<Sort | null>(null)

  const sortedRows = useMemo(() => {
    if (sort === null) return rows
    const sign = sort.direction === 'asc' ? 1 : -1
    // Address tie-break keeps the on-screen order reproducible; range selection will depend on it.
    return [...rows].sort(
      (a, b) => sign * compareBy(a, b, sort.key) || compareOrdinal(a.senderAddress, b.senderAddress),
    )
  }, [rows, sort])

  function toggleSort(column: Column) {
    setSort((current) =>
      current?.key === column.sortKey
        ? { key: column.sortKey, direction: current.direction === 'asc' ? 'desc' : 'asc' }
        : { key: column.sortKey, direction: column.firstDirection },
    )
  }

  return (
    <table>
      <thead>
        <tr>
          {columns.map((column) => (
            <th key={column.sortKey}>
              <button type="button" onClick={() => toggleSort(column)}>
                {column.label}
                {sort?.key === column.sortKey ? (sort.direction === 'asc' ? ' ▲' : ' ▼') : ''}
              </button>
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {sortedRows.map((row) => (
          <tr key={row.senderAddress}>
            {columns.map((column) => (
              <td key={column.sortKey}>{column.render(row)}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  )
}