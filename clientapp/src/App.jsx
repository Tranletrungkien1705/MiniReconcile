import React, { useEffect, useState } from 'react'
import { Routes, Route, NavLink, Outlet } from 'react-router-dom'
import { api, fmtMoney, fmtDate, SSTATUS } from './api'

function Badge({ text, css }) { return <span className={`badge ${css || 'secondary'}`}>{text}</span> }
function Flash({ msg }) { return msg ? <div className={`flash ${msg.ok ? 'ok' : 'err'}`}>{msg.text}</div> : null }
function Modal({ title, onClose, wide, children }) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal" style={wide ? { maxWidth: 740 } : undefined} onClick={e => e.stopPropagation()}>
        <div className="row" style={{ marginBottom: 12 }}><h2 style={{ flex: 1, margin: 0 }}>{title}</h2>
          <button className="btn gray sm" style={{ flex: 'none' }} onClick={onClose}>Đóng</button></div>{children}
      </div>
    </div>
  )
}
function Field({ label, children }) { return <div style={{ flex: 1 }}><label>{label}</label>{children}</div> }
function AgingBars({ a }) {
  const items = [['Trong hạn', a.current], ['1-30N', a.b1_30], ['31-60N', a.b31_60], ['61-90N', a.b61_90], ['>90N', a.over90]]
  const max = Math.max(1, ...items.map(i => i[1]))
  const colors = ['var(--success)', 'var(--info)', 'var(--warning)', '#f97316', 'var(--danger)']
  return <div className="funnel">{items.map((it, i) => (
    <div className="bar" key={i}><div className="lbl">{it[0]}</div>
      <div className="track"><div className="fill" style={{ width: `${(it[1] / max) * 100}%`, background: colors[i] }} /></div>
      <div className="n" style={{ width: 100, fontSize: 12 }}>{fmtMoney(it[1])}</div></div>))}</div>
}

function Layout() {
  return (
    <>
      <nav className="nav"><span className="brand">📊 MiniReconcile</span>
        <NavLink to="/" end>Tổng quan</NavLink><NavLink to="/partners">Đối tác/Công nợ</NavLink><NavLink to="/statements">Bảng đối soát</NavLink></nav>
      <div className="wrap"><Outlet /></div>
    </>
  )
}

function Dashboard() {
  const [d, setD] = useState(null); const [cache, setCache] = useState('')
  useEffect(() => { api.dashboard().then(r => { setD(r.data); setCache(r.cache) }) }, [])
  if (!d) return <p className="muted">Đang tải…</p>
  return (
    <>
      <h1>Tổng quan công nợ {cache && <span className="pill">cache: {cache}</span>}</h1>
      <div className="grid kpis" style={{ marginBottom: 18 }}>
        <div className="kpi"><div className="v" style={{ fontSize: 20, color: 'var(--danger)' }}>{fmtMoney(d.totalReceivable)}</div><div className="l">Tổng phải thu</div></div>
        <div className="kpi"><div className="v">{d.partnerCount}</div><div className="l">Đối tác</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--warning)' }}>{d.overdueCount}</div><div className="l">Đối tác quá hạn</div></div>
        <div className="kpi"><div className="v">{d.pendingStatements}</div><div className="l">Bảng chờ xác nhận</div></div>
      </div>
      <div className="card"><h2>Phân tích tuổi nợ (aging)</h2><AgingBars a={d.aging} /></div>
      <div className="card"><h2>Top công nợ</h2>
        <table><thead><tr><th>Đối tác</th><th className="right">Dư nợ</th><th className="right">Quá hạn</th></tr></thead>
          <tbody>{d.top.map((t, i) => <tr key={i}><td>{t.partner}</td><td className="right">{fmtMoney(t.balance)}</td><td className="right" style={{ color: t.overdue > 0 ? 'var(--danger)' : undefined }}>{fmtMoney(t.overdue)}</td></tr>)}</tbody></table>
      </div>
    </>
  )
}

function Partners() {
  const [rows, setRows] = useState([]); const [q, setQ] = useState(''); const [open, setOpen] = useState(null); const [show, setShow] = useState(false)
  const load = () => api.partners(q).then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 'none' }}>Đối tác / Công nợ</h1><div className="sp" />
        <input style={{ maxWidth: 220 }} placeholder="Tìm tên/mã…" value={q} onChange={e => setQ(e.target.value)} onKeyDown={e => e.key === 'Enter' && load()} />
        <button className="btn ghost sm" style={{ flex: 'none' }} onClick={load}>Tìm</button>
        <button className="btn sm" style={{ flex: 'none' }} onClick={() => setShow(true)}>+ Thêm đối tác</button></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Mã</th><th>Tên</th><th>SĐT</th><th className="right">Dư nợ</th><th className="right">Quá hạn</th></tr></thead>
          <tbody>{rows.map(p => (
            <tr key={p.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(p.id)}>
              <td>{p.code}</td><td>{p.name}</td><td>{p.phone || '—'}</td>
              <td className="right"><b>{fmtMoney(p.balance)}</b></td><td className="right" style={{ color: p.overdue > 0 ? 'var(--danger)' : undefined }}>{fmtMoney(p.overdue)}</td></tr>))}
            {rows.length === 0 && <tr><td colSpan={5} className="muted" style={{ padding: 20 }}>Chưa có đối tác.</td></tr>}</tbody></table>
      </div>
      {open && <PartnerDetail id={open} onClose={() => setOpen(null)} onChanged={load} />}
      {show && <PartnerForm onClose={() => setShow(false)} onSaved={() => { setShow(false); load() }} />}
    </>
  )
}

function PartnerDetail({ id, onClose, onChanged }) {
  const [p, setP] = useState(null); const [msg, setMsg] = useState(null)
  const [e, setE] = useState({ docNo: '', type: 0, amount: 0, dueDate: '' })
  const load = () => api.partner(id).then(r => setP(r.data))
  useEffect(() => { load() }, [id])
  const flash = (ok, text) => { setMsg({ ok, text }); setTimeout(() => setMsg(null), 3000) }
  const add = async () => { try { const r = await api.addEntry(id, { docNo: e.docNo, type: Number(e.type), amount: Number(e.amount), dueDate: e.dueDate || null }); flash(true, r.data.msg); setE({ docNo: '', type: 0, amount: 0, dueDate: '' }); load(); onChanged() } catch (er) { flash(false, er.message) } }
  if (!p) return <Modal title="…" onClose={onClose}><p className="muted">Đang tải…</p></Modal>
  return (
    <Modal title={`${p.name} (${p.code})`} onClose={onClose} wide>
      <Flash msg={msg} />
      <div className="row" style={{ marginBottom: 8 }}><span className="pill" style={{ flex: 'none' }}>Đầu kỳ: {fmtMoney(p.openingBalance)}</span>
        <span className="pill" style={{ flex: 'none', background: '#fee2e2' }}>Dư nợ: {fmtMoney(p.balance)}</span></div>
      <AgingBars a={p.aging} />
      <div className="section-t">Sổ cái công nợ</div>
      <div style={{ maxHeight: 260, overflow: 'auto' }}>
        <table><thead><tr><th>Ngày</th><th>Chứng từ</th><th>Loại</th><th className="right">Số tiền</th><th>Hạn</th></tr></thead>
          <tbody>{p.ledger.map(l => (<tr key={l.id}><td>{fmtDate(l.entryDate)}</td><td>{l.docNo}{l.frozen && <span className="pill" style={{ marginLeft: 4 }}>đã đối soát</span>}</td>
            <td style={{ color: l.type === 0 ? 'var(--danger)' : 'var(--success)' }}>{l.typeText}</td><td className="right">{fmtMoney(l.amount)}</td><td>{fmtDate(l.dueDate)}</td></tr>))}</tbody></table>
      </div>
      <div className="card" style={{ background: '#f8fafc', marginTop: 8 }}>
        <div className="section-t">Ghi bút toán</div>
        <div className="row"><Field label="Chứng từ"><input value={e.docNo} onChange={ev => setE({ ...e, docNo: ev.target.value })} /></Field>
          <Field label="Loại"><select value={e.type} onChange={ev => setE({ ...e, type: ev.target.value })}><option value={0}>Ghi nợ (hóa đơn)</option><option value={1}>Thanh toán</option></select></Field>
          <Field label="Số tiền"><input type="number" value={e.amount} onChange={ev => setE({ ...e, amount: ev.target.value })} /></Field>
          <Field label="Hạn TT"><input type="date" value={e.dueDate} onChange={ev => setE({ ...e, dueDate: ev.target.value })} /></Field></div>
        <div style={{ marginTop: 10 }}><button className="btn sm" onClick={add} disabled={!e.amount}>Ghi</button></div>
      </div>
    </Modal>
  )
}

function PartnerForm({ onClose, onSaved }) {
  const [f, setF] = useState({ name: '', code: '', phone: '', openingBalance: 0 }); const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => { try { if (!f.name) { setErr('Cần tên'); return } await api.createPartner({ ...f, openingBalance: Number(f.openingBalance) }); onSaved() } catch (e) { setErr(e.message) } }
  return (
    <Modal title="Thêm đối tác" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Tên *"><input value={f.name} onChange={e => up('name', e.target.value)} /></Field>
        <Field label="Mã"><input value={f.code} onChange={e => up('code', e.target.value)} /></Field></div>
      <div className="row"><Field label="SĐT"><input value={f.phone} onChange={e => up('phone', e.target.value)} /></Field>
        <Field label="Dư nợ đầu kỳ"><input type="number" value={f.openingBalance} onChange={e => up('openingBalance', e.target.value)} /></Field></div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Lưu</button></div>
    </Modal>
  )
}

function Statements() {
  const [rows, setRows] = useState([]); const [status, setStatus] = useState(''); const [open, setOpen] = useState(null); const [show, setShow] = useState(false)
  const load = () => api.statements(status === '' ? null : Number(status)).then(r => setRows(r.data))
  useEffect(() => { load() }, [status])
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 'none' }}>Bảng đối soát</h1><div className="sp" />
        <select style={{ maxWidth: 160 }} value={status} onChange={e => setStatus(e.target.value)}><option value="">— Trạng thái —</option>{SSTATUS.map((s, i) => <option key={i} value={i}>{s}</option>)}</select>
        <button className="btn sm" style={{ flex: 'none' }} onClick={() => setShow(true)}>+ Lập bảng</button></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Số</th><th>Đối tác</th><th>Kỳ</th><th className="right">Đầu kỳ</th><th className="right">Nợ</th><th className="right">Có</th><th className="right">Cuối kỳ</th><th>Trạng thái</th></tr></thead>
          <tbody>{rows.map(s => (
            <tr key={s.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(s.id)}>
              <td>{s.no}</td><td>{s.partner}</td><td>{fmtDate(s.fromDate)}–{fmtDate(s.toDate)}</td>
              <td className="right">{fmtMoney(s.openingBalance)}</td><td className="right">{fmtMoney(s.totalDebit)}</td><td className="right">{fmtMoney(s.totalCredit)}</td>
              <td className="right"><b>{fmtMoney(s.closingBalance)}</b></td><td><Badge text={s.statusText} css={s.statusCss} /></td></tr>))}
            {rows.length === 0 && <tr><td colSpan={8} className="muted" style={{ padding: 20 }}>Chưa có bảng đối soát.</td></tr>}</tbody></table>
      </div>
      {open && <StatementDetail id={open} onClose={() => setOpen(null)} onChanged={load} />}
      {show && <StatementForm onClose={() => setShow(false)} onSaved={() => { setShow(false); load() }} />}
    </>
  )
}

function StatementDetail({ id, onClose, onChanged }) {
  const [s, setS] = useState(null); const [msg, setMsg] = useState(null); const [note, setNote] = useState('')
  const load = () => api.statement(id).then(r => setS(r.data))
  useEffect(() => { load() }, [id])
  const flash = (ok, text) => { setMsg({ ok, text }); setTimeout(() => setMsg(null), 3000) }
  const act = async (status, n) => { try { const r = await api.setStatus(id, status, n); flash(true, r.data.msg); load(); onChanged() } catch (e) { flash(false, e.message) } }
  if (!s) return <Modal title="…" onClose={onClose}><p className="muted">Đang tải…</p></Modal>
  return (
    <Modal title={`Bảng ${s.no} — ${s.partner}`} onClose={onClose} wide>
      <Flash msg={msg} />
      <div className="row" style={{ marginBottom: 8 }}><Badge text={s.statusText} css="secondary" /><span className="pill" style={{ flex: 'none' }}>{fmtDate(s.fromDate)} – {fmtDate(s.toDate)}</span></div>
      <dl className="dl"><dt>Đầu kỳ</dt><dd>{fmtMoney(s.openingBalance)}</dd><dt>Phát sinh nợ</dt><dd>{fmtMoney(s.totalDebit)}</dd>
        <dt>Phát sinh có</dt><dd>{fmtMoney(s.totalCredit)}</dd><dt style={{ fontWeight: 700 }}>Cuối kỳ</dt><dd style={{ fontWeight: 700, color: 'var(--danger)' }}>{fmtMoney(s.closingBalance)}</dd></dl>
      {s.disputeNote && <div className="flash err">Khiếu nại: {s.disputeNote}</div>}
      <div className="section-t">Chi tiết ({s.lines.length} bút toán)</div>
      <div style={{ maxHeight: 220, overflow: 'auto' }}>
        <table><thead><tr><th>Ngày</th><th>Chứng từ</th><th>Loại</th><th className="right">Số tiền</th></tr></thead>
          <tbody>{s.lines.map((l, i) => <tr key={i}><td>{fmtDate(l.entryDate)}</td><td>{l.docNo}</td><td style={{ color: l.type === 0 ? 'var(--danger)' : 'var(--success)' }}>{l.typeText}</td><td className="right">{fmtMoney(l.amount)}</td></tr>)}</tbody></table>
      </div>
      <div className="row" style={{ gap: 6, marginTop: 12 }}>
        {s.status === 0 && <button className="btn sm" onClick={() => act(1)}>Gửi đối tác</button>}
        {s.status === 1 && <><button className="btn sm" onClick={() => act(2)}>Xác nhận</button>
          <input placeholder="Lý do khiếu nại" value={note} onChange={e => setNote(e.target.value)} style={{ maxWidth: 200 }} />
          <button className="btn gray sm" style={{ flex: 'none' }} onClick={() => act(3, note)}>Khiếu nại</button></>}
        {s.status === 2 && <button className="btn gray sm" onClick={() => act(4)}>Chốt</button>}
      </div>
    </Modal>
  )
}

function StatementForm({ onClose, onSaved }) {
  const [partners, setPartners] = useState([]); const [f, setF] = useState({ partnerId: '', from: '', to: '' }); const [err, setErr] = useState('')
  useEffect(() => { api.partners().then(r => { setPartners(r.data); if (r.data[0]) setF(s => ({ ...s, partnerId: r.data[0].id })) }) }, [])
  const save = async () => { try { if (!f.partnerId) { setErr('Chọn đối tác'); return } await api.createStatement({ partnerId: Number(f.partnerId), from: f.from || null, to: f.to || null }); onSaved() } catch (e) { setErr(e.message) } }
  return (
    <Modal title="Lập bảng đối soát" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <Field label="Đối tác"><select value={f.partnerId} onChange={e => setF({ ...f, partnerId: e.target.value })}>{partners.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></Field>
      <div className="row"><Field label="Từ ngày"><input type="date" value={f.from} onChange={e => setF({ ...f, from: e.target.value })} /></Field>
        <Field label="Đến ngày"><input type="date" value={f.to} onChange={e => setF({ ...f, to: e.target.value })} /></Field></div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Lập bảng (đóng băng bút toán)</button></div>
    </Modal>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="partners" element={<Partners />} />
        <Route path="statements" element={<Statements />} />
      </Route>
    </Routes>
  )
}
