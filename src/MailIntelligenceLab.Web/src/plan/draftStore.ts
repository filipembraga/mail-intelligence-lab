export type Draft = Record<string, string>

// Keyed by plan file name, so a draft can never be applied to a different plan.
const keyPrefix = 'mail-intelligence-lab:draft:'

export function loadDraft(fileName: string): Draft {
  try {
    const raw = localStorage.getItem(keyPrefix + fileName)
    return raw ? (JSON.parse(raw) as Draft) : {}
  } catch {
    return {}
  }
}

export function saveDraft(fileName: string, draft: Draft): void {
  try {
    if (Object.keys(draft).length === 0) {
      localStorage.removeItem(keyPrefix + fileName)
    } else {
      localStorage.setItem(keyPrefix + fileName, JSON.stringify(draft))
    }
  } catch {
    // Storage full or disabled: the draft simply won't survive a reload.
  }
}