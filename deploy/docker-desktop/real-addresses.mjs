// Real visitor addresses for Tesria under Docker Desktop (Mac or Windows).
//
// Docker Desktop accepts every connection to a published port itself and
// passes it into its Linux VM from one address, 192.168.65.1, so Tesria
// sees every device (this Mac, a PC, a phone) as the same visitor: its
// sign-in limits, security alerts and audit log cannot tell them apart,
// and blocking one would block all (the owner, 2026-09-24). Docker Desktop
// on Windows works the same way. Only the computer itself still sees the
// real address. This runs on it (plain Node, nothing to install), outside
// Docker, listens where Caddy used to (80 and 443), and hands each
// connection to Caddy with the real address in front of it, using the
// PROXY protocol (v1, one text line). TLS is untouched: the bytes after
// that line are the visitor's own, and Caddy's certificates stay as they
// are.
//
//   node deploy/docker-desktop/real-addresses.mjs
//
// Caddy must then be on the loopback ports below and accept the PROXY line
// from where these connections arrive: docker-compose.real-addresses.yml
// beside this file does both. Verified on a Mac, 2026-09-24: a request to
// the Mac's network address was recorded with that address instead of
// Docker Desktop's gateway.
//
// Settings (environment): LISTEN_HTTP (80), LISTEN_HTTPS (443),
// TARGET_HOST (127.0.0.1), TARGET_HTTP (18080), TARGET_HTTPS (18443).

import net from 'node:net'

const env = (name, fallback) => process.env[name] || fallback
const TARGET_HOST = env('TARGET_HOST', '127.0.0.1')
const ROUTES = [
  { listen: Number(env('LISTEN_HTTP', 80)), target: Number(env('TARGET_HTTP', 18080)) },
  { listen: Number(env('LISTEN_HTTPS', 443)), target: Number(env('TARGET_HTTPS', 18443)) },
]

/** An IPv4 address seen through an IPv6 socket, written the way Caddy expects. */
const plain = (address) => (address?.startsWith('::ffff:') ? address.slice(7) : address)

/** The PROXY protocol v1 line for a connection, or UNKNOWN if its addresses cannot be read. */
export function proxyLine(socket) {
  const source = plain(socket.remoteAddress)
  const dest = plain(socket.localAddress)
  if (!source || !dest || !socket.remotePort || !socket.localPort) return 'PROXY UNKNOWN\r\n'
  const family = net.isIPv4(source) && net.isIPv4(dest) ? 'TCP4'
    : net.isIPv6(source) && net.isIPv6(dest) ? 'TCP6'
    : null
  if (!family) return 'PROXY UNKNOWN\r\n'
  return `PROXY ${family} ${source} ${dest} ${socket.remotePort} ${socket.localPort}\r\n`
}

function forward(client, targetPort) {
  const upstream = net.connect({ host: TARGET_HOST, port: targetPort })
  client.pause()
  upstream.once('connect', () => {
    upstream.write(proxyLine(client))
    client.pipe(upstream)
    upstream.pipe(client)
    client.resume()
  })
  // Either side failing ends both; nothing is logged per connection, since
  // a visitor closing a tab is not news.
  const end = () => { client.destroy(); upstream.destroy() }
  client.on('error', end)
  upstream.on('error', end)
  client.on('close', () => upstream.destroy())
  upstream.on('close', () => client.destroy())
}

if (process.argv[1] && import.meta.url.endsWith(process.argv[1].split('/').pop())) {
  for (const { listen, target } of ROUTES) {
    const server = net.createServer({ allowHalfOpen: false }, (socket) => forward(socket, target))
    server.on('error', (err) => {
      console.error(`[real-addresses] cannot listen on ${listen}: ${err.message}`)
      process.exit(1)
    })
    // `::` with IPv6 on also accepts IPv4 on macOS, so one listener covers both.
    server.listen({ port: listen, host: '::', ipv6Only: false }, () =>
      console.log(`[real-addresses] ${listen} -> ${TARGET_HOST}:${target}, with each visitor's real address`))
  }
}
