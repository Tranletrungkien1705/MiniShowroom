import React, { useEffect, useState } from 'react'
import { Routes, Route, NavLink, Link, useParams, Outlet } from 'react-router-dom'
import { api, fmtMoney, fmtDate, fmtDateTime, STAGES, SOURCES, PAYMETHODS } from './api'

// ───────────────────────── Dùng chung ─────────────────────────
function Badge({ text, css }) { return <span className={`badge ${css || 'secondary'}`}>{text}</span> }
function Flash({ msg }) { return msg ? <div className={`flash ${msg.ok ? 'ok' : 'err'}`}>{msg.text}</div> : null }
function Modal({ title, onClose, children }) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="row" style={{ marginBottom: 12 }}><h2 style={{ flex: 1, margin: 0 }}>{title}</h2>
          <button className="btn gray sm" style={{ flex: 'none' }} onClick={onClose}>Đóng</button></div>
        {children}
      </div>
    </div>
  )
}
function Field({ label, children }) { return <div style={{ flex: 1 }}><label>{label}</label>{children}</div> }

// ───────────────────────── Layout ─────────────────────────
function Layout() {
  return (
    <>
      <nav className="nav">
        <span className="brand">🚗 MiniShowroom</span>
        <NavLink to="/" end>Tổng quan</NavLink>
        <NavLink to="/leads">Khách hàng</NavLink>
        <NavLink to="/deals">Thương vụ</NavLink>
        <NavLink to="/models">Mẫu xe</NavLink>
      </nav>
      <div className="wrap"><Outlet /></div>
    </>
  )
}

// ───────────────────────── Dashboard ─────────────────────────
function Dashboard() {
  const [d, setD] = useState(null)
  const [cache, setCache] = useState('')
  useEffect(() => { api.dashboard().then(r => { setD(r.data); setCache(r.cache) }) }, [])
  if (!d) return <p className="muted">Đang tải…</p>
  const maxF = Math.max(1, ...d.funnel.map(f => f.count))
  return (
    <>
      <h1>Tổng quan bán hàng {cache && <span className="pill">cache: {cache}</span>}</h1>
      <div className="grid kpis" style={{ marginBottom: 18 }}>
        <div className="kpi"><div className="v">{d.leads}</div><div className="l">Tổng khách</div></div>
        <div className="kpi"><div className="v">{d.active}</div><div className="l">Đang theo đuổi</div></div>
        <div className="kpi"><div className="v">{d.testDrivesWeek}</div><div className="l">Lái thử (7 ngày)</div></div>
        <div className="kpi"><div className="v">{d.dealsOpen}</div><div className="l">Thương vụ mở</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--success)', fontSize: 20 }}>{fmtMoney(d.revenueMonth)}</div><div className="l">Doanh thu tháng</div></div>
      </div>
      <div className="card funnel">
        <h2>Phễu bán hàng</h2>
        {d.funnel.map(f => (
          <div className="bar" key={f.stage}>
            <div className="lbl">{f.stageText}</div>
            <div className="track"><div className="fill" style={{ width: `${(f.count / maxF) * 100}%` }} /></div>
            <div className="n">{f.count}</div>
          </div>
        ))}
      </div>
    </>
  )
}

// ───────────────────────── Danh sách khách (Lead) ─────────────────────────
function Leads() {
  const [rows, setRows] = useState([])
  const [stage, setStage] = useState('')
  const [q, setQ] = useState('')
  const [models, setModels] = useState([])
  const [show, setShow] = useState(false)
  const load = () => api.leads(stage === '' ? null : Number(stage), q).then(r => setRows(r.data))
  useEffect(() => { load() }, [stage])
  useEffect(() => { api.models(true).then(r => setModels(r.data)) }, [])
  return (
    <>
      <div className="toolbar">
        <h1 style={{ margin: 0, flex: 'none' }}>Khách hàng</h1>
        <div className="sp" />
        <select style={{ maxWidth: 180 }} value={stage} onChange={e => setStage(e.target.value)}>
          <option value="">— Tất cả giai đoạn —</option>
          {STAGES.map((s, i) => <option key={i} value={i}>{s}</option>)}
        </select>
        <input style={{ maxWidth: 220 }} placeholder="Tìm tên / SĐT / mã…" value={q}
          onChange={e => setQ(e.target.value)} onKeyDown={e => e.key === 'Enter' && load()} />
        <button className="btn ghost sm" style={{ flex: 'none' }} onClick={load}>Tìm</button>
        <button className="btn sm" style={{ flex: 'none' }} onClick={() => setShow(true)}>+ Thêm khách</button>
      </div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table>
          <thead><tr><th>Mã</th><th>Tên</th><th>SĐT</th><th>Nguồn</th><th>Xe quan tâm</th><th>Giai đoạn</th><th>NV Sale</th></tr></thead>
          <tbody>
            {rows.map(l => (
              <tr key={l.id}>
                <td><Link to={`/leads/${l.id}`}>{l.code}</Link></td>
                <td>{l.name}</td><td>{l.phone}</td><td>{l.sourceText}</td>
                <td>{l.modelName || '—'}</td><td><Badge text={l.stageText} css={l.stageCss} /></td><td>{l.salesPerson || '—'}</td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={7} className="muted" style={{ padding: 20 }}>Không có khách nào.</td></tr>}
          </tbody>
        </table>
      </div>
      {show && <LeadForm models={models} onClose={() => setShow(false)} onSaved={() => { setShow(false); load() }} />}
    </>
  )
}

function LeadForm({ models, onClose, onSaved }) {
  const [f, setF] = useState({ name: '', phone: '', email: '', identityNo: '', address: '', source: 0, modelId: '', salesPerson: '', note: '' })
  const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => {
    try {
      await api.createLead({ ...f, source: Number(f.source), modelId: f.modelId ? Number(f.modelId) : null })
      onSaved()
    } catch (e) { setErr(e.message) }
  }
  return (
    <Modal title="Thêm khách hàng" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Họ tên *"><input value={f.name} onChange={e => up('name', e.target.value)} /></Field>
        <Field label="SĐT *"><input value={f.phone} onChange={e => up('phone', e.target.value)} /></Field></div>
      <div className="row"><Field label="Email"><input value={f.email} onChange={e => up('email', e.target.value)} /></Field>
        <Field label="CCCD"><input value={f.identityNo} onChange={e => up('identityNo', e.target.value)} /></Field></div>
      <Field label="Địa chỉ"><input value={f.address} onChange={e => up('address', e.target.value)} /></Field>
      <div className="row"><Field label="Nguồn"><select value={f.source} onChange={e => up('source', e.target.value)}>{SOURCES.map((s, i) => <option key={i} value={i}>{s}</option>)}</select></Field>
        <Field label="Xe quan tâm"><select value={f.modelId} onChange={e => up('modelId', e.target.value)}><option value="">—</option>{models.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select></Field></div>
      <div className="row"><Field label="NV Sale"><input value={f.salesPerson} onChange={e => up('salesPerson', e.target.value)} /></Field>
        <Field label="Ghi chú"><input value={f.note} onChange={e => up('note', e.target.value)} /></Field></div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Lưu khách hàng</button></div>
    </Modal>
  )
}

// ───────────────────────── Chi tiết khách ─────────────────────────
function LeadDetail() {
  const { id } = useParams()
  const [d, setD] = useState(null)
  const [models, setModels] = useState([])
  const [msg, setMsg] = useState(null)
  const [tdShow, setTdShow] = useState(false)
  const [dealShow, setDealShow] = useState(false)
  const load = () => api.lead(id).then(r => setD(r.data))
  useEffect(() => { load(); api.models(true).then(r => setModels(r.data)) }, [id])
  if (!d) return <p className="muted">Đang tải…</p>
  const l = d.lead
  const flash = (ok, text) => { setMsg({ ok, text }); setTimeout(() => setMsg(null), 3000) }
  const advance = async (to) => { try { await api.advance(id, to); flash(true, 'Đã chuyển giai đoạn.'); load() } catch (e) { flash(false, e.message) } }
  return (
    <>
      <div className="toolbar"><Link to="/leads" className="btn gray sm" style={{ flex: 'none' }}>← Danh sách</Link>
        <h1 style={{ margin: 0 }}>{l.name} <span className="pill">{l.code}</span></h1></div>
      <Flash msg={msg} />
      <div className="grid" style={{ gridTemplateColumns: '1fr 1fr' }}>
        <div className="card">
          <h2>Thông tin</h2>
          <dl className="dl">
            <dt>Giai đoạn</dt><dd><Badge text={l.stageText} css={l.stageCss} /></dd>
            <dt>SĐT</dt><dd>{l.phone}</dd>
            <dt>Email</dt><dd>{l.email || '—'}</dd>
            <dt>CCCD</dt><dd>{l.identityNo || '—'}</dd>
            <dt>Địa chỉ</dt><dd>{l.address || '—'}</dd>
            <dt>Nguồn</dt><dd>{l.sourceText}</dd>
            <dt>Xe quan tâm</dt><dd>{l.modelName || '—'}</dd>
            <dt>NV Sale</dt><dd>{l.salesPerson || '—'}</dd>
            <dt>Ngày tạo</dt><dd>{fmtDate(l.createdAt)}</dd>
          </dl>
          {l.stage < 5 && l.stage !== 6 && (
            <div style={{ marginTop: 14 }}>
              <div className="section-t">Chuyển giai đoạn</div>
              <div className="row" style={{ gap: 6 }}>
                {l.stage < 1 && <button className="btn ghost sm" onClick={() => advance(1)}>Đã liên hệ</button>}
                <button className="btn gray sm" onClick={() => advance(6)}>Mất khách</button>
              </div>
            </div>
          )}
        </div>
        <div>
          <div className="card">
            <div className="row"><h2 style={{ flex: 1 }}>Lái thử</h2><button className="btn sm" style={{ flex: 'none' }} onClick={() => setTdShow(true)}>+ Đặt lịch</button></div>
            {d.testDrives.length === 0 ? <p className="muted">Chưa có lịch lái thử.</p> :
              <table><tbody>{d.testDrives.map(t => (
                <tr key={t.id}><td>{t.modelName}</td><td>{fmtDateTime(t.scheduledAt)}</td>
                  <td className="right"><span className="pill">{t.statusText}</span></td></tr>))}</tbody></table>}
          </div>
          <div className="card">
            <div className="row"><h2 style={{ flex: 1 }}>Thương vụ / Báo giá</h2><button className="btn sm" style={{ flex: 'none' }} onClick={() => setDealShow(true)}>+ Báo giá</button></div>
            {d.deals.length === 0 ? <p className="muted">Chưa có báo giá.</p> :
              <table><tbody>{d.deals.map(dl => (
                <tr key={dl.id}><td><Link to={`/deals?open=${dl.id}`}>{dl.code}</Link></td>
                  <td>{dl.modelName}</td><td className="right">{fmtMoney(dl.totalPayable)}</td>
                  <td className="right"><Badge text={dl.statusText} css={dl.statusCss} /></td></tr>))}</tbody></table>}
          </div>
        </div>
      </div>
      {tdShow && <TestDriveForm leadId={id} models={models} onClose={() => setTdShow(false)} onSaved={() => { setTdShow(false); flash(true, 'Đã đặt lịch lái thử.'); load() }} />}
      {dealShow && <DealForm leadId={id} models={models} interestedModelId={l.modelId} onClose={() => setDealShow(false)} onSaved={() => { setDealShow(false); flash(true, 'Đã tạo báo giá.'); load() }} />}
    </>
  )
}

function TestDriveForm({ leadId, models, onClose, onSaved }) {
  const [modelId, setModelId] = useState(models[0]?.id || '')
  const [at, setAt] = useState('')
  const [note, setNote] = useState('')
  const [err, setErr] = useState('')
  const save = async () => {
    try {
      const iso = at && at.length === 16 ? at + ':00' : at
      await api.bookTestDrive({ leadId: Number(leadId), modelId: Number(modelId), scheduledAt: iso || new Date(Date.now() + 86400000).toISOString(), note })
      onSaved()
    } catch (e) { setErr(e.message) }
  }
  return (
    <Modal title="Đặt lịch lái thử" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <Field label="Mẫu xe"><select value={modelId} onChange={e => setModelId(e.target.value)}>{models.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select></Field>
      <Field label="Thời gian"><input type="datetime-local" value={at} onChange={e => setAt(e.target.value)} /></Field>
      <Field label="Ghi chú"><input value={note} onChange={e => setNote(e.target.value)} /></Field>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Đặt lịch</button></div>
    </Modal>
  )
}

function DealForm({ leadId, models, interestedModelId, onClose, onSaved }) {
  const [f, setF] = useState({ modelId: interestedModelId || models[0]?.id || '', price: '', discount: 0, depositAmount: 0, paymentMethod: 0, accessoriesAmount: 0 })
  const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const m = models.find(x => x.id === Number(f.modelId))
  const price = f.price !== '' ? Number(f.price) : (m?.listPrice || 0)
  const vat = Math.round(price * 0.10), reg = Math.round(price * 0.10), ins = Math.round(price * 0.015), plate = 20000000
  const total = price - Number(f.discount || 0) + vat + reg + plate + ins + Number(f.accessoriesAmount || 0)
  const save = async () => {
    try {
      await api.createDeal({
        leadId: Number(leadId), modelId: Number(f.modelId), price, discount: Number(f.discount || 0),
        depositAmount: Number(f.depositAmount || 0), accessoriesAmount: Number(f.accessoriesAmount || 0),
        paymentMethod: Number(f.paymentMethod), loanAmount: 0
      })
      onSaved()
    } catch (e) { setErr(e.message) }
  }
  return (
    <Modal title="Tạo báo giá (giá lăn bánh)" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <Field label="Mẫu xe"><select value={f.modelId} onChange={e => up('modelId', e.target.value)}>{models.map(m => <option key={m.id} value={m.id}>{m.name} — {fmtMoney(m.listPrice)}</option>)}</select></Field>
      <div className="row"><Field label="Giá xe"><input type="number" value={f.price} placeholder={m?.listPrice} onChange={e => up('price', e.target.value)} /></Field>
        <Field label="Chiết khấu"><input type="number" value={f.discount} onChange={e => up('discount', e.target.value)} /></Field></div>
      <div className="row"><Field label="Đặt cọc"><input type="number" value={f.depositAmount} onChange={e => up('depositAmount', e.target.value)} /></Field>
        <Field label="Phụ kiện"><input type="number" value={f.accessoriesAmount} onChange={e => up('accessoriesAmount', e.target.value)} /></Field></div>
      <Field label="Thanh toán"><select value={f.paymentMethod} onChange={e => up('paymentMethod', e.target.value)}>{PAYMETHODS.map((s, i) => <option key={i} value={i}>{s}</option>)}</select></Field>
      <div className="card" style={{ background: '#f8fafc', marginTop: 14, marginBottom: 0 }}>
        <div className="dl">
          <dt>VAT (10%)</dt><dd className="right">{fmtMoney(vat)}</dd>
          <dt>Lệ phí trước bạ</dt><dd className="right">{fmtMoney(reg)}</dd>
          <dt>Phí biển số</dt><dd className="right">{fmtMoney(plate)}</dd>
          <dt>Bảo hiểm</dt><dd className="right">{fmtMoney(ins)}</dd>
          <dt style={{ fontWeight: 700 }}>TỔNG LĂN BÁNH</dt><dd className="right" style={{ fontWeight: 700, color: 'var(--brand)' }}>{fmtMoney(total)}</dd>
        </div>
      </div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Tạo báo giá</button></div>
    </Modal>
  )
}

// ───────────────────────── Thương vụ ─────────────────────────
function Deals() {
  const [rows, setRows] = useState([])
  const [status, setStatus] = useState('')
  const [open, setOpen] = useState(null)
  const load = () => api.deals(status === '' ? null : Number(status)).then(r => setRows(r.data))
  useEffect(() => { load() }, [status])
  useEffect(() => {
    const p = new URLSearchParams(window.location.hash.split('?')[1] || '')
    if (p.get('open')) setOpen(Number(p.get('open')))
  }, [])
  const DS = ['Báo giá', 'Đã cọc', 'Đã giao', 'Đã hủy']
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 'none' }}>Thương vụ</h1><div className="sp" />
        <select style={{ maxWidth: 180 }} value={status} onChange={e => setStatus(e.target.value)}>
          <option value="">— Tất cả —</option>{DS.map((s, i) => <option key={i} value={i}>{s}</option>)}</select></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table>
          <thead><tr><th>Mã</th><th>Khách</th><th>Xe</th><th className="right">Tổng lăn bánh</th><th className="right">Còn lại</th><th>VIN</th><th>Trạng thái</th></tr></thead>
          <tbody>
            {rows.map(d => (
              <tr key={d.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(d.id)}>
                <td>{d.code}</td><td>{d.leadName}</td><td>{d.modelName}</td>
                <td className="right">{fmtMoney(d.totalPayable)}</td><td className="right">{fmtMoney(d.remaining)}</td>
                <td>{d.vin || '—'}</td><td><Badge text={d.statusText} css={d.statusCss} /></td>
              </tr>))}
            {rows.length === 0 && <tr><td colSpan={7} className="muted" style={{ padding: 20 }}>Không có thương vụ.</td></tr>}
          </tbody>
        </table>
      </div>
      {open && <DealDetail id={open} onClose={() => setOpen(null)} onChanged={load} />}
    </>
  )
}

function DealDetail({ id, onClose, onChanged }) {
  const [d, setD] = useState(null)
  const [msg, setMsg] = useState(null)
  const [assign, setAssign] = useState(false)
  const load = () => api.deal(id).then(r => setD(r.data))
  useEffect(() => { load() }, [id])
  const flash = (ok, text) => { setMsg({ ok, text }); setTimeout(() => setMsg(null), 2500) }
  const act = async (to) => { try { await api.dealAction(id, to); flash(true, 'Đã cập nhật.'); load(); onChanged() } catch (e) { flash(false, e.message) } }
  if (!d) return <Modal title="…" onClose={onClose}><p className="muted">Đang tải…</p></Modal>
  return (
    <Modal title={`Thương vụ ${d.code}`} onClose={onClose}>
      <Flash msg={msg} />
      <div className="row" style={{ marginBottom: 8 }}><Badge text={d.statusText} css={d.statusCss} />
        <span className="pill" style={{ flex: 'none' }}>{d.paymentMethodText}</span></div>
      <dl className="dl">
        <dt>Khách</dt><dd>{d.buyerName} · {d.buyerPhone}</dd>
        <dt>Xe</dt><dd>{d.modelName} {d.color ? `· ${d.color}` : ''}</dd>
        <dt>Giá sau CK</dt><dd>{fmtMoney(d.finalPrice)}</dd>
        <dt>VAT + phí</dt><dd>{fmtMoney(d.vatAmount + d.registrationFee + d.plateFee + d.insuranceAmount + d.accessoriesAmount)}</dd>
        <dt style={{ fontWeight: 700 }}>Tổng lăn bánh</dt><dd style={{ fontWeight: 700, color: 'var(--brand)' }}>{fmtMoney(d.totalPayable)}</dd>
        <dt>Đã cọc</dt><dd>{fmtMoney(d.depositAmount)}</dd>
        <dt>Còn phải thu</dt><dd style={{ color: 'var(--warning)', fontWeight: 600 }}>{fmtMoney(d.remaining)}</dd>
        <dt>Hẹn giao</dt><dd>{fmtDate(d.expectedDelivery)}</dd>
        {d.vin && <><dt>VIN</dt><dd>{d.vin}</dd><dt>Số máy / khung</dt><dd>{d.engineNo} / {d.chassisNo}</dd><dt>Biển số</dt><dd>{d.licensePlate || '—'}</dd></>}
        {d.deliveredAt && <><dt>Đã giao</dt><dd>{fmtDateTime(d.deliveredAt)}</dd></>}
        {d.insurancePolicyCode && <><dt>Bảo hiểm TNDS</dt><dd>{d.insurancePolicyCode} <span className="muted" style={{ fontSize: 11 }}>(tự lập qua MiniInsurance)</span></dd></>}
        {d.warrantyStampCode && <><dt>Tem chính hãng</dt><dd>{d.warrantyStampCode} <span className="muted" style={{ fontSize: 11 }}>(tem QR + kích hoạt BH qua MiniStamp)</span></dd></>}
        {d.loyaltyInfo && <><dt>Điểm thưởng</dt><dd>{d.loyaltyInfo} <span className="muted" style={{ fontSize: 11 }}>(MiniLoyalty)</span></dd></>}
      </dl>
      <div className="section-t">Thao tác</div>
      <div className="row" style={{ gap: 6 }}>
        {d.status === 0 && <button className="btn sm" onClick={() => act(1)}>Ghi nhận đặt cọc</button>}
        {d.status === 1 && !d.vin && <button className="btn ghost sm" onClick={() => setAssign(true)}>Gán xe (VIN)</button>}
        {d.status === 1 && <button className="btn sm" disabled={!d.vin} onClick={() => act(2)}>Giao xe</button>}
        {(d.status === 0 || d.status === 1) && <button className="btn gray sm" onClick={() => act(3)}>Hủy</button>}
        {d.status === 1 && !d.vin && <span className="muted" style={{ flex: 'none', fontSize: 12, alignSelf: 'center' }}>Cần gán VIN trước khi giao</span>}
      </div>
      {assign && <AssignForm id={id} defColor={d.color} onClose={() => setAssign(false)} onSaved={() => { setAssign(false); flash(true, 'Đã gán xe.'); load() }} />}
    </Modal>
  )
}

function AssignForm({ id, defColor, onClose, onSaved }) {
  const [f, setF] = useState({ vin: '', engineNo: '', chassisNo: '', color: defColor || '', licensePlate: '' })
  const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => { try { await api.assignVehicle(id, f); onSaved() } catch (e) { setErr(e.message) } }
  return (
    <Modal title="Gán xe cho thương vụ" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <Field label="Số VIN *"><input value={f.vin} onChange={e => up('vin', e.target.value)} /></Field>
      <div className="row"><Field label="Số máy"><input value={f.engineNo} onChange={e => up('engineNo', e.target.value)} /></Field>
        <Field label="Số khung"><input value={f.chassisNo} onChange={e => up('chassisNo', e.target.value)} /></Field></div>
      <div className="row"><Field label="Màu"><input value={f.color} onChange={e => up('color', e.target.value)} /></Field>
        <Field label="Biển số"><input value={f.licensePlate} onChange={e => up('licensePlate', e.target.value)} /></Field></div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Lưu định danh xe</button></div>
    </Modal>
  )
}

// ───────────────────────── Mẫu xe ─────────────────────────
function Models() {
  const [rows, setRows] = useState([])
  const [show, setShow] = useState(false)
  const load = () => api.models().then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 1 }}>Mẫu xe</h1>
        <button className="btn sm" style={{ flex: 'none' }} onClick={() => setShow(true)}>+ Thêm mẫu</button></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table>
          <thead><tr><th>Mã</th><th>Tên</th><th>Phiên bản</th><th>Đời</th><th>Chỗ</th><th>Phân khúc</th><th className="right">Giá niêm yết</th></tr></thead>
          <tbody>{rows.map(m => (
            <tr key={m.id}><td>{m.code}</td><td>{m.name}</td><td>{m.variant || '—'}</td>
              <td>{m.modelYear || '—'}</td><td>{m.seats || '—'}</td><td>{m.segment || '—'}</td>
              <td className="right">{fmtMoney(m.listPrice)}</td></tr>))}</tbody>
        </table>
      </div>
      {show && <ModelForm onClose={() => setShow(false)} onSaved={() => { setShow(false); load() }} />}
    </>
  )
}

function ModelForm({ onClose, onSaved }) {
  const [f, setF] = useState({ name: '', code: '', variant: '', listPrice: 0, color: '', modelYear: 2024, fuelType: 'Xăng', seats: 5, segment: '' })
  const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => {
    try {
      await api.createModel({ ...f, listPrice: Number(f.listPrice), modelYear: Number(f.modelYear) || null, seats: Number(f.seats) || null })
      onSaved()
    } catch (e) { setErr(e.message) }
  }
  return (
    <Modal title="Thêm mẫu xe" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Tên *"><input value={f.name} onChange={e => up('name', e.target.value)} /></Field>
        <Field label="Mã"><input value={f.code} onChange={e => up('code', e.target.value)} /></Field></div>
      <div className="row"><Field label="Phiên bản"><input value={f.variant} onChange={e => up('variant', e.target.value)} /></Field>
        <Field label="Giá niêm yết"><input type="number" value={f.listPrice} onChange={e => up('listPrice', e.target.value)} /></Field></div>
      <div className="row"><Field label="Đời"><input type="number" value={f.modelYear} onChange={e => up('modelYear', e.target.value)} /></Field>
        <Field label="Số chỗ"><input type="number" value={f.seats} onChange={e => up('seats', e.target.value)} /></Field>
        <Field label="Phân khúc"><input value={f.segment} onChange={e => up('segment', e.target.value)} /></Field></div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Lưu mẫu xe</button></div>
    </Modal>
  )
}

// ───────────────────────── App ─────────────────────────
export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="leads" element={<Leads />} />
        <Route path="leads/:id" element={<LeadDetail />} />
        <Route path="deals" element={<Deals />} />
        <Route path="models" element={<Models />} />
      </Route>
    </Routes>
  )
}
