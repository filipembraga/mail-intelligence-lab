// Every HTTP call goes through this module, so pointing at a hosted API later changes one file.
export type AuthStatus = { httpStatus: number; body: string }

export async function getAuthStatus(): Promise<AuthStatus> {
  const response = await fetch('/api/auth')
  return { httpStatus: response.status, body: await response.text() }
}