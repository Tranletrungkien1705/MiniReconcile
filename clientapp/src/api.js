const base = '/api/v1'
async function req(path, opts = {}) {
  const res = await fetch(base + path, {
    headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin',
    ...opts, body: opts.body ? JSON.stringify(opts.body) : undefined
  })
  const text = await res.text(); const data = text ? JSON.parse(text) : null
  if (!res.ok) throw new Error(data?.error || `Lỗi ${res.status}`)
  return { data, cache: res.headers.get('X-Cache') }
}
export const api = {
  dashboard: () => req('/dashboard'),
  partners: (q) => req(`/partners${q ? `?q=${encodeURIComponent(q)}` : ''}`),
  partner: (id) => req(`/partners/${id}`),
  createPartner: (b) => req('/partners', { method: 'POST', body: b }),
  addEntry: (id, b) => req(`/partners/${id}/entries`, { method: 'POST', body: b }),
  statements: (status) => req(`/statements${status != null ? `?status=${status}` : ''}`),
  statement: (id) => req(`/statements/${id}`),
  createStatement: (b) => req('/statements', { method: 'POST', body: b }),
  setStatus: (id, status, note) => req(`/statements/${id}/status`, { method: 'POST', body: { status, note } })
}
export const fmtMoney = (n) => (n ?? 0).toLocaleString('vi-VN') + 'đ'
export const fmtDate = (s) => s ? new Date(s).toLocaleDateString('vi-VN') : '—'
export const SSTATUS = ['Nháp', 'Đã gửi', 'Đã xác nhận', 'Khiếu nại', 'Đã chốt']
