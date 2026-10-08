// Every HTTP call goes through this module, so pointing at a hosted API later changes one file.
export type AuthStatus = { httpStatus: number; body: string }

export async function getAuthStatus(): Promise<AuthStatus> {
  const response = await fetch('/api/auth')
  return { httpStatus: response.status, body: await response.text() }
}

export type PlanRow = {
  senderAddress: string
  senderName: string
  messageCount: number
  messagesWithAttachmentsCount: number
  attachmentFileCount: number
  totalAttachmentSizeMB: number
  totalAttachmentSizeBytes: number
  averageAgeYears: number
  oldestReceivedDate: string
  newestReceivedDate: string
  action: string
}

export type Plan = { fileName: string; freezeBoundUtc: string; rows: PlanRow[] }

export type ApiError = { httpStatus: number; body: unknown }

export type PlanResult = { ok: true; plan: Plan } | { ok: false; error: ApiError }

export async function getPlan(): Promise<PlanResult> {
  const response = await fetch('/api/plan')
  const body: unknown = await response.json().catch(() => null)
  return response.ok
    ? { ok: true, plan: body as Plan }
    : { ok: false, error: { httpStatus: response.status, body } }
}

export const PlanActions = {
  keep: '',
  delete: 'delete',
  permanentDelete: 'permanent-delete',
} as const