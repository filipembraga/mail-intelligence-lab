import { useEffect, useState } from 'react'
import { getAuthStatus, getPlan, type AuthStatus, type PlanResult } from './api/client'
import PlanTable from './plan/PlanTable'

export default function App() {
  const [auth, setAuth] = useState<AuthStatus | null>(null)
  const [plan, setPlan] = useState<PlanResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getAuthStatus().then(setAuth).catch((e: unknown) => setError(String(e)))
    getPlan().then(setPlan).catch((e: unknown) => setError(String(e)))
  }, [])

  return (
    <main>
      <h1>mail-intelligence-lab</h1>
      {error && <p>Request failed: {error}</p>}
      {auth && (
        <p>
          /api/auth → {auth.httpStatus}: <code>{auth.body}</code>
        </p>
      )}
      {plan === null && !error && <p>Loading plan…</p>}
      {plan && !plan.ok && (
        <p>
          /api/plan → {plan.error.httpStatus}: <code>{JSON.stringify(plan.error.body)}</code>
        </p>
      )}
      {plan && plan.ok && (
        <>
          <p>
            {plan.plan.fileName} — {plan.plan.rows.length} senders
          </p>
          <PlanTable key={plan.plan.fileName} fileName={plan.plan.fileName} rows={plan.plan.rows} />
        </>
      )}
    </main>
  )
}