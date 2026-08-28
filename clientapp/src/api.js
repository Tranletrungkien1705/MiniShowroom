// Lớp gọi API JSON tới backend ASP.NET (cùng origin). Gửi kèm cookie org_key (multi-tenant).
const base = '/api/v1'

async function req(path, opts = {}) {
  const res = await fetch(base + path, {
    headers: { 'Content-Type': 'application/json' },
    credentials: 'same-origin',
    ...opts,
    body: opts.body ? JSON.stringify(opts.body) : undefined
  })
  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  if (!res.ok) throw new Error(data?.error || `Lỗi ${res.status}`)
  return { data, cache: res.headers.get('X-Cache') }
}

export const api = {
  dashboard: () => req('/dashboard'),
  models: (activeOnly = false) => req(`/models?activeOnly=${activeOnly}`),
  createModel: (b) => req('/models', { method: 'POST', body: b }),
  leads: (stage, q) => req(`/leads?${stage != null ? `stage=${stage}&` : ''}${q ? `q=${encodeURIComponent(q)}` : ''}`),
  lead: (id) => req(`/leads/${id}`),
  createLead: (b) => req('/leads', { method: 'POST', body: b }),
  advance: (id, to) => req(`/leads/${id}/advance`, { method: 'POST', body: { to } }),
  bookTestDrive: (b) => req('/testdrives', { method: 'POST', body: b }),
  setTdStatus: (id, status) => req(`/testdrives/${id}/status`, { method: 'POST', body: { status } }),
  deals: (status) => req(`/deals${status != null ? `?status=${status}` : ''}`),
  deal: (id) => req(`/deals/${id}`),
  createDeal: (b) => req('/deals', { method: 'POST', body: b }),
  dealAction: (id, to) => req(`/deals/${id}/action`, { method: 'POST', body: { to } }),
  assignVehicle: (id, b) => req(`/deals/${id}/assign-vehicle`, { method: 'POST', body: b })
}

export const fmtMoney = (n) => (n ?? 0).toLocaleString('vi-VN') + ' ₫'
export const fmtDate = (s) => s ? new Date(s).toLocaleDateString('vi-VN') : '—'
export const fmtDateTime = (s) => s ? new Date(s).toLocaleString('vi-VN') : '—'

export const STAGES = ['Mới', 'Đã liên hệ', 'Đã lái thử', 'Đã báo giá', 'Đã đặt cọc', 'Đã giao xe', 'Mất khách']
export const SOURCES = ['Đến showroom', 'Hotline', 'Facebook', 'Website', 'Giới thiệu']
export const PAYMETHODS = ['Tiền mặt', 'Chuyển khoản', 'Trả góp']
