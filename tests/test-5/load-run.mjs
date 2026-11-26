import { spawn, execFileSync } from 'node:child_process'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
const P = process.env.PROJECT ?? 'distributed-ecommerce-platform'
const dir = path.dirname(fileURLToPath(import.meta.url))
const [mode = 'none', afterMs = '8000', target = 'api-2', base = 'http://nginx'] = process.argv.slice(2)
const started = Date.now()
const since = () => `+${((Date.now() - started) / 1000).toFixed(1)} s`
const k6 = spawn('docker', ['run', '--rm', '--network', `${P}_default`, '-e', `BASE=${base}`, '-v', `${dir}:/scripts`,
  'grafana/k6', 'run', '/scripts/load.js'], { stdio: 'inherit' })
if (mode !== 'none') setTimeout(() => {
  console.log(`\n>>> ${since()}  docker ${mode} ${target}`)
  execFileSync('docker', [mode, `${P}-${target}-1`])
  console.log(`>>> ${since()}  ${target} is down`)
}, Number(afterMs))
k6.on('exit', code => console.log(`k6 finished, exit code ${code}, after ${since()}`))
