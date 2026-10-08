import { memo, useEffect, useMemo, useState } from 'react'
import type { MouseEvent as ReactMouseEvent } from 'react'
import { PlanActions, type PlanRow } from '../api/client'
import { loadDraft, saveDraft, type Draft } from './draftStore'

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
  render: (row: PlanRow, action: string) => string | number
}

const columns: Column[] = [
  { label: 'Sender', sortKey: 'senderName', firstDirection: 'asc', render: (row) => row.senderName },
  { label: 'Address', sortKey: 'senderAddress', firstDirection: 'asc', render: (row) => row.senderAddress },
  { label: 'Messages', sortKey: 'messageCount', firstDirection: 'desc', render: (row) => row.messageCount },
  { label: 'With attachments', sortKey: 'messagesWithAttachmentsCount', firstDirection: 'desc', render: (row) => row.messagesWithAttachmentsCount },
  { label: 'Files', sortKey: 'attachmentFileCount', firstDirection: 'desc', render: (row) => row.attachmentFileCount },
  // Sorted by bytes: the MB value is rounded to an integer, so many rows would tie.
  { label: 'Size (MB)', sortKey: 'totalAttachmentSizeBytes', firstDirection: 'desc', render: (row) => row.totalAttachmentSizeMB },
  { label: 'Avg age (y)', sortKey: 'averageAgeYears', firstDirection: 'desc', render: (row) => row.averageAgeYears },
  { label: 'Oldest', sortKey: 'oldestReceivedDate', firstDirection: 'asc', render: (row) => row.oldestReceivedDate },
  { label: 'Newest', sortKey: 'newestReceivedDate', firstDirection: 'asc', render: (row) => row.newestReceivedDate },
  // Displays the pending action, but sorts by the one in the file: a row must not jump away while it is being marked.
  { label: 'Action', sortKey: 'action', firstDirection: 'desc', render: (_row, action) => action },
]

type Sort = { key: SortKey; direction: Direction }

function compareOrdinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0
}

function compareBy(left: PlanRow, right: PlanRow, key: SortKey): number {
  const leftValue = left[key]
  const rightValue = right[key]
  if (typeof leftValue === 'number' && typeof rightValue === 'number') return leftValue - rightValue
  // Names are read by humans: accents and case should not split one person into two places.
  if (key === 'senderName') {
    return String(leftValue).localeCompare(String(rightValue), undefined, { sensitivity: 'base' })
  }
  return compareOrdinal(String(leftValue), String(rightValue))
}

// A draft is only a diff against the file: entries that no longer differ, or that name a
// sender no longer in the plan, are dropped instead of resurrected.
function restoreDraft(fileName: string, fileActions: Map<string, string>): Draft {
  return Object.fromEntries(
    Object.entries(loadDraft(fileName)).filter(
      ([address, action]) => fileActions.has(address) && fileActions.get(address) !== action,
    ),
  )
}

type RowProps = { row: PlanRow; action: string; isSelected: boolean; isPending: boolean }

// Props are all stable references or primitives, so memo skips every row whose
// selection or action did not change. Rows take no callbacks for the same reason:
// clicks are handled once, on the tbody.
const PlanTableRow = memo(function PlanTableRow({ row, action, isSelected, isPending }: RowProps) {
  const className = [isSelected && 'selected', isPending && 'pending'].filter(Boolean).join(' ')
  return (
    <tr data-address={row.senderAddress} className={className || undefined}>
      <td>
        <input type="checkbox" checked={isSelected} readOnly aria-label={`Select ${row.senderAddress}`} />
      </td>
      {columns.map((column) => (
        <td key={column.sortKey}>{column.render(row, action)}</td>
      ))}
    </tr>
  )
})

export default function PlanTable({ fileName, rows }: { fileName: string; rows: PlanRow[] }) {
  const fileActions = useMemo(() => new Map(rows.map((row) => [row.senderAddress, row.action])), [rows])

  const [sort, setSort] = useState<Sort | null>(null)
  const [selected, setSelected] = useState<Set<string>>(() => new Set())
  const [anchor, setAnchor] = useState<string | null>(null)
  const [pending, setPending] = useState<Draft>(() => restoreDraft(fileName, fileActions))

  useEffect(() => {
    saveDraft(fileName, pending)
  }, [fileName, pending])

  const sortedRows = useMemo(() => {
    if (sort === null) return rows
    const sign = sort.direction === 'asc' ? 1 : -1
    // Address tie-break keeps the on-screen order reproducible; range selection depends on it.
    return [...rows].sort(
      (left, right) =>
        sign * compareBy(left, right, sort.key) || compareOrdinal(left.senderAddress, right.senderAddress),
    )
  }, [rows, sort])

  const pendingCount = Object.keys(pending).length

  function toggleSort(column: Column) {
    setSort((current) =>
      current?.key === column.sortKey
        ? { key: column.sortKey, direction: current.direction === 'asc' ? 'desc' : 'asc' }
        : { key: column.sortKey, direction: column.firstDirection },
    )
  }

  function handleBodyClick(event: ReactMouseEvent<HTMLTableSectionElement>) {
    // A drag that selected text is a copy gesture, not a row click.
    const textSelection = window.getSelection()
    if (textSelection !== null && !textSelection.isCollapsed) return
    if (!(event.target instanceof Element)) return
    const address = event.target.closest('tr')?.dataset.address
    if (!address) return

    if (event.shiftKey && anchor !== null) {
      const fromIndex = sortedRows.findIndex((row) => row.senderAddress === anchor)
      const toIndex = sortedRows.findIndex((row) => row.senderAddress === address)
      if (fromIndex !== -1 && toIndex !== -1) {
        // The range is whatever lies between the two rows on screen, in the current order.
        const range = sortedRows.slice(Math.min(fromIndex, toIndex), Math.max(fromIndex, toIndex) + 1)
        setSelected((current) => {
          const next = new Set(current)
          for (const row of range) next.add(row.senderAddress)
          return next
        })
        return
      }
    }

    setSelected((current) => {
      const next = new Set(current)
      if (next.has(address)) next.delete(address)
      else next.add(address)
      return next
    })
    setAnchor(address)
  }

  function preventShiftTextSelection(event: ReactMouseEvent<HTMLTableSectionElement>) {
    if (event.shiftKey) event.preventDefault()
  }

  function clearSelection() {
    setSelected(new Set())
    setAnchor(null)
  }

  function applyToSelected(action: string) {
    setPending((current) => {
      const next = { ...current }
      for (const address of selected) {
        // Setting a row back to what the file already says is not a change.
        if (fileActions.get(address) === action) delete next[address]
        else next[address] = action
      }
      return next
    })
    clearSelection()
  }

  function discardChanges() {
    if (window.confirm(`Discard ${pendingCount} unsaved change(s)?`)) setPending({})
  }

  const nothingSelected = selected.size === 0

  return (
    <>
      <div className="toolbar">
        <span>
          {selected.size} selected · {pendingCount} unsaved
        </span>
        <button type="button" disabled={nothingSelected} onClick={() => applyToSelected(PlanActions.delete)}>
          Mark delete
        </button>
        <button type="button" disabled={nothingSelected} onClick={() => applyToSelected(PlanActions.permanentDelete)}>
          Mark permanent-delete
        </button>
        <button type="button" disabled={nothingSelected} onClick={() => applyToSelected(PlanActions.keep)}>
          Clear mark
        </button>
        <button type="button" disabled={nothingSelected} onClick={clearSelection}>
          Clear selection
        </button>
        <button type="button" disabled={pendingCount === 0} onClick={discardChanges}>
          Discard unsaved
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th />
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
        <tbody onClick={handleBodyClick} onMouseDown={preventShiftTextSelection}>
          {sortedRows.map((row) => {
            const isPending = Object.hasOwn(pending, row.senderAddress)
            return (
              <PlanTableRow
                key={row.senderAddress}
                row={row}
                action={isPending ? pending[row.senderAddress] : row.action}
                isSelected={selected.has(row.senderAddress)}
                isPending={isPending}
              />
            )
          })}
        </tbody>
      </table>
    </>
  )
}