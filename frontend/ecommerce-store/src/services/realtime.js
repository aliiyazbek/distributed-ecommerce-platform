import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { toOrder } from './orderService.js'
import { readToken } from './session.js'

const retryDelay = 3000

function connect(path, handlers, onReconnected) {
  const connection = new HubConnectionBuilder()
    .withUrl(path, { accessTokenFactory: readToken })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => retryDelay })
    .configureLogging(LogLevel.Warning)
    .build()

  Object.entries(handlers).forEach(([event, handler]) => connection.on(event, handler))
  connection.onreconnected(onReconnected)

  let stopped = false
  const start = () => {
    if (!stopped) connection.start().catch(() => setTimeout(start, retryDelay))
  }
  const firstStart = setTimeout(start)

  return () => {
    stopped = true
    clearTimeout(firstStart)
    connection.stop()
  }
}

export function watchOrderStatus(orderId, onChange, onReconnected) {
  const id = orderId.toLowerCase()
  return connect('/hubs/orders', {
    OrderStatusChanged: change => {
      if (change.orderId === id) onChange(change)
    },
  }, onReconnected)
}

export function watchNewOrders(onOrder, onReconnected) {
  return connect('/hubs/admin-orders', {
    OrderCreated: order => onOrder(toOrder(order)),
  }, onReconnected)
}
