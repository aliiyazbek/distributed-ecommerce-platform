import { useEffect, useState } from 'react'

export default function useLoad(load) {
  const [attempt, setAttempt] = useState(0)
  const [result, setResult] = useState(null)

  useEffect(() => {
    const controller = new AbortController()
    load(controller.signal).then(
      data => setResult({ load, attempt, data }),
      error => {
        if (!controller.signal.aborted) setResult({ load, attempt, error })
      },
    )
    return () => controller.abort()
  }, [load, attempt])

  const current = result?.load === load && result.attempt === attempt ? result : null

  return {
    loading: !current,
    data: current?.data,
    error: current?.error,
    retry: () => setAttempt(value => value + 1),
    setData: data => setResult({ load, attempt, data }),
  }
}
