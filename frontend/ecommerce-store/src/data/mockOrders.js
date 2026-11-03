// Frontend fixtures only. Replace with API data when the backend is ready.
// Dates, counts, totals and statuses follow the supplied Figma reference.
// Its tiny order/customer identifiers are unclear, so these are explicit demo IDs.
// Dates have no timezone in the design and are kept as display labels here.
const orderItems = [
  [
    { id: 'headphones', name: 'Studio Over-Ear Headphones', unitPrice: 149, quantity: 1 },
    { id: 'kettle', name: 'Matte Gooseneck Electric Kettle', unitPrice: 89.50, quantity: 1 },
  ],
  // Remaining product names are demo fixtures, not customer or API data.
  [{ id: 'demo-speakers', name: 'Demo Desktop Speakers', unitPrice: 119.25, quantity: 2 }],
  [{ id: 'demo-notebook', name: 'Demo Notebook Set', unitPrice: 34, quantity: 1 }],
  [
    { id: 'demo-lamp', name: 'Demo Desk Lamp', unitPrice: 59.95, quantity: 2 },
    { id: 'demo-organizer', name: 'Demo Desk Organizer', unitPrice: 38, quantity: 2 },
  ],
  [{ id: 'demo-chair', name: 'Demo Office Chair', unitPrice: 146, quantity: 1 }],
  [{ id: 'demo-cushion', name: 'Demo Seat Cushion', unitPrice: 56.50, quantity: 2 }],
]

export const mockOrders = [
  { id: 'DEMO-001', customerId: 'DEMO-101', datePlaced: 'Nov 2, 2026, 6:06 PM', itemCount: 2, total: 238.50, status: 'Pending' },
  { id: 'DEMO-002', customerId: 'DEMO-102', datePlaced: 'May 14, 2025, 10:24 AM', itemCount: 2, total: 238.50, status: 'Shipped' },
  { id: 'DEMO-003', customerId: 'DEMO-103', datePlaced: 'May 12, 2025, 4:08 PM', itemCount: 1, total: 34.00, status: 'Pending' },
  { id: 'DEMO-004', customerId: 'DEMO-102', datePlaced: 'May 2, 2025, 11:40 AM', itemCount: 4, total: 195.90, status: 'Delivered' },
  { id: 'DEMO-005', customerId: 'DEMO-104', datePlaced: 'May 13, 2025, 6:12 PM', itemCount: 1, total: 146.00, status: 'Pending' },
  { id: 'DEMO-006', customerId: 'DEMO-105', datePlaced: 'May 5, 2025, 1:50 PM', itemCount: 2, total: 113.00, status: 'Cancelled' },
].map((order, index) => ({
  ...order,
  // The details screenshot makes the first order's identifiers readable.
  orderNumber: index === 0 ? 'B3A089C5' : order.id,
  customerId: index === 0 ? 'C94810E2' : order.customerId,
  items: orderItems[index],
}))
