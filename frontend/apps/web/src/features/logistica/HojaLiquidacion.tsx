import { useParams } from '@tanstack/react-router';
import { useHojaLiquidacion, type HojaLiquidacionDto } from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda } from '@tg/shared';
import { Boton } from '@tg/ui';
import type { ReactNode } from 'react';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';

/** RN-LOG-22: hoja de liquidación con el formato del canal, dinero, créditos, gastos y 4 firmas. */
export function HojaLiquidacion() {
  const { id } = useParams({ strict: false }) as { id: string };
  const consulta = useHojaLiquidacion(id);
  const folio = consulta.data?.liquidacion.folio ?? '';
  return (
    <Pagina>
      <div className="print:hidden">
        <EncabezadoPagina
          titulo={`Hoja de liquidación ${folio}`}
          migas={[
            { etiqueta: 'Logística' },
            { etiqueta: 'Liquidaciones', a: '/logistica/liquidaciones' },
            { etiqueta: folio, a: `/logistica/liquidaciones/${id}` },
            { etiqueta: 'Hoja' },
          ]}
          acciones={
            <Boton icono="print" onClick={() => window.print()}>
              Imprimir
            </Boton>
          }
        />
      </div>
      <EstadoConsulta cargando={consulta.isLoading} error={consulta.error}>
        {consulta.data && <Hoja hoja={consulta.data} />}
      </EstadoConsulta>
    </Pagina>
  );
}

function Celda({ children, derecha }: { children: ReactNode; derecha?: boolean }) {
  return <td className={`border border-[#999] px-2 py-1 ${derecha ? 'text-right tabular-nums' : ''}`}>{children}</td>;
}

function Encabezado({ children, derecha }: { children: ReactNode; derecha?: boolean }) {
  return <th className={`border border-[#999] bg-[#eee] px-2 py-1 font-semibold ${derecha ? 'text-right' : 'text-left'}`}>{children}</th>;
}

function Renglon({ etiqueta, valor }: { etiqueta: string; valor: ReactNode }) {
  return (
    <tr>
      <td className="border border-[#999] px-2 py-1">{etiqueta}</td>
      <td className="border border-[#999] px-2 py-1 text-right tabular-nums">{valor}</td>
    </tr>
  );
}

function Hoja({ hoja }: { hoja: HojaLiquidacionDto }) {
  const l = hoja.liquidacion;
  const completo = l.formato === 'Completo';
  const totalImporte = l.partidas.reduce((s, p) => s + Math.round(p.importeVenta * 100), 0) / 100;
  const totalCreditos = hoja.creditos.reduce((s, c) => s + Math.round(c.total * 100), 0) / 100;
  return (
    <article className="mx-auto max-w-[1000px] bg-white p-6 text-[13px] text-black print:max-w-none print:p-0">
      <header className="mb-4 flex flex-wrap items-start justify-between gap-2">
        <div>
          <h1 className="m-0 text-lg font-bold">Tortillas Guerreras · Liquidación de ruta</h1>
          <p className="m-0">
            Ruta {l.rutaCodigo} · {l.rutaNombre} · Vendedor: {l.choferNombre ?? '—'}
          </p>
        </div>
        <div className="text-right">
          <p className="m-0 font-semibold">{l.folio}</p>
          <p className="m-0">Fecha: {formatoFecha(l.fechaOperativa)}</p>
          <p className="m-0">Estado: {l.estado}</p>
        </div>
      </header>

      <table className="mb-4 w-full border-collapse">
        <thead>
          <tr>
            <Encabezado>Producto</Encabezado>
            <Encabezado derecha>Carga</Encabezado>
            <Encabezado derecha>Sobrante</Encabezado>
            {completo && <Encabezado derecha>Devolución</Encabezado>}
            {completo && <Encabezado derecha>Recarga</Encabezado>}
            {completo && <Encabezado derecha>Cambios</Encabezado>}
            <Encabezado derecha>Venta</Encabezado>
            <Encabezado derecha>Total $</Encabezado>
            <Encabezado derecha>Diferencia</Encabezado>
          </tr>
        </thead>
        <tbody>
          {l.partidas.map((p) => (
            <tr key={`${p.productoId}|${p.lote}`}>
              <Celda>
                {p.sku} {p.producto}
                {p.lote ? ` · ${p.lote}` : ''}
              </Celda>
              <Celda derecha>{formatoCantidad(completo ? p.cargadoInicial : p.cargado)}</Celda>
              <Celda derecha>{p.sobranteContado == null ? '' : formatoCantidad(p.sobranteContado)}</Celda>
              {completo && <Celda derecha>{formatoCantidad(p.devueltoClientesVendible + p.devueltoClientesNoVendible)}</Celda>}
              {completo && <Celda derecha>{formatoCantidad(p.recargado)}</Celda>}
              {completo && <Celda derecha>{formatoCantidad(p.cambiosEntregados)}</Celda>}
              <Celda derecha>{formatoCantidad(p.vendido)}</Celda>
              <Celda derecha>{formatoMoneda(p.importeVenta)}</Celda>
              <Celda derecha>{p.diferencia == null || p.diferencia === 0 ? '' : formatoCantidad(p.diferencia)}</Celda>
            </tr>
          ))}
          <tr className="font-semibold">
            <Celda>Total</Celda>
            <td colSpan={completo ? 5 : 2} className="border border-[#999]" />
            <Celda derecha>{formatoCantidad(l.partidas.reduce((s, p) => s + p.vendido, 0))}</Celda>
            <Celda derecha>{formatoMoneda(totalImporte)}</Celda>
            <td className="border border-[#999]" />
          </tr>
        </tbody>
      </table>

      <div className="mb-4 grid gap-4 sm:grid-cols-2 print:grid-cols-2">
        <table className="w-full border-collapse">
          <thead>
            <tr>
              <Encabezado>Dinero</Encabezado>
              <Encabezado derecha>Importe</Encabezado>
            </tr>
          </thead>
          <tbody>
            <Renglon etiqueta="Venta total" valor={formatoMoneda(l.totalVendido)} />
            <Renglon etiqueta="Ventas a crédito" valor={formatoMoneda(l.totalCredito)} />
            <Renglon etiqueta="Transferencias" valor={formatoMoneda(l.totalTransferencias)} />
            <Renglon etiqueta="Cheques" valor={formatoMoneda(l.totalCheques)} />
            <Renglon etiqueta="Contado en efectivo" valor={formatoMoneda(l.ventasContadoEfectivo)} />
            <Renglon etiqueta="Cobros en efectivo" valor={formatoMoneda(l.cobrosEfectivo)} />
            <Renglon etiqueta="Gastos autorizados" valor={`−${formatoMoneda(l.totalGastos)}`} />
            <Renglon etiqueta="Efectivo esperado" valor={formatoMoneda(l.efectivoEsperado)} />
            <Renglon etiqueta="Efectivo entregado" valor={formatoMoneda(l.efectivoEntregado)} />
            <Renglon etiqueta="Diferencia de efectivo" valor={formatoMoneda(l.diferenciaEfectivo)} />
            <Renglon etiqueta="Diferencia de precios" valor={formatoMoneda(l.diferenciaPrecios)} />
          </tbody>
        </table>

        <div className="flex flex-col gap-4">
          <table className="w-full border-collapse">
            <thead>
              <tr>
                <Encabezado>Créditos del día</Encabezado>
                <Encabezado>Folio</Encabezado>
                <Encabezado derecha>Importe</Encabezado>
              </tr>
            </thead>
            <tbody>
              {hoja.creditos.length === 0 && (
                <tr>
                  <td colSpan={3} className="border border-[#999] px-2 py-1 text-center">
                    Sin créditos
                  </td>
                </tr>
              )}
              {hoja.creditos.map((c) => (
                <tr key={c.folio}>
                  <Celda>{c.cliente}</Celda>
                  <Celda>{c.folio}</Celda>
                  <Celda derecha>{formatoMoneda(c.total)}</Celda>
                </tr>
              ))}
              {hoja.creditos.length > 0 && <Renglon etiqueta="Total créditos" valor={formatoMoneda(totalCreditos)} />}
            </tbody>
          </table>

          <table className="w-full border-collapse">
            <thead>
              <tr>
                <Encabezado>Gastos</Encabezado>
                <Encabezado>Comprobante</Encabezado>
                <Encabezado derecha>Importe</Encabezado>
              </tr>
            </thead>
            <tbody>
              {l.gastos.length === 0 && (
                <tr>
                  <td colSpan={3} className="border border-[#999] px-2 py-1 text-center">
                    Sin gastos
                  </td>
                </tr>
              )}
              {l.gastos.map((g) => (
                <tr key={g.id}>
                  <Celda>
                    {g.concepto}
                    {g.autorizado ? '' : ' (no autorizado)'}
                  </Celda>
                  <Celda>{g.comprobante ?? ''}</Celda>
                  <Celda derecha>{formatoMoneda(g.importe)}</Celda>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {l.notas && <p className="mb-4 whitespace-pre-line">Notas: {l.notas}</p>}

      <footer className="mt-12 grid grid-cols-2 gap-x-8 gap-y-12 sm:grid-cols-4 print:grid-cols-4">
        {[
          ['Almacén', hoja.firmaAlmacen],
          ['Vendedor', hoja.firmaVendedor],
          ['Supervisor', hoja.firmaSupervisor],
          ['Liquidaciones', hoja.firmaLiquidaciones],
        ].map(([rol, nombre]) => (
          <div key={rol} className="border-t border-black pt-1 text-center">
            <p className="m-0 font-semibold">{rol}</p>
            <p className="m-0">{nombre ?? ''}</p>
          </div>
        ))}
      </footer>
      <p className="mt-6 text-right text-[11px] text-[#555]">Generada {formatoFechaHora(hoja.generadaEn)}</p>
    </article>
  );
}
