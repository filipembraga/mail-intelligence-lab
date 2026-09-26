import { useEffect, useState } from 'react'
import { getAuthStatus, type AuthStatus } from './api/client'

export default function App() {
  const [status, setStatus] = useState<AuthStatus | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getAuthStatus()
      .then(setStatus)
      .catch((e: unknown) => setError(String(e)))
  }, [])

  return (
    <main>
      <h1>mail-intelligence-lab</h1>
      {error && <p>Request failed: {error}</p>}
      {status && (
        <p>
          /api/auth → {status.httpStatus}: <code>{status.body}</code>
        </p>
      )}
      {!status && !error && <p>Checking authentication…</p>}
    </main>
  )
}