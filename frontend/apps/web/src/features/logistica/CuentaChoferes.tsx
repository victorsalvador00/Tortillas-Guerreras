import {
  mensajeError,
  useCancelarCargoChofer,
  useCrearCargoChofer,
  useListarCargosChofer,
  useListarVendedores,
  type CargoChoferDto,
  type EstadoCargoChofer,
  type TipoCargoChofer,
} from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFecha, formatoMoneda, sumarDias } from '@tg/shared';
import { Aviso, Boton, Campo, Dialogo, EtiquetaEstado, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

export const TIPOS_CARGO: { valor: TipoCargoChofer; etiqueta: string }[] = [
  { valor: 'ProductoACobro', etiqueta: 'Producto a cobro' },
  { valor: 'FaltanteProducto', etiqueta: 'Faltante de producto' },
  { valor: 'FaltanteEfectivo', etiqueta: 'Faltante de efectivo' },
  { valor: 'DiferenciaPrecio', etiqueta: 'Diferencia de precio' },
  { valor: 'Otro', etiqueta: 'Otro' },
];

export const etiquetaTipoCargo = (tipo: TipoCargoChofer) => TIPOS_CARGO.find((t) => t.valor === tipo)?.etiqueta ?? tipo;

/** RN-LOG-27: cancelar un cargo pendiente con motivo. */
function DialogoCancelarCargo({ cargo, onCerrar }: { cargo: CargoChoferDto; onCerrar: () => void }) {
  const cancelar = useCancelarCargoChofer();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo={`Cancelar cargo ${cargo.folio}`}
      descripcion={`${etiquetaTipoCargo(cargo.tipo)} por ${formatoMoneda(cargo.importe)} a ${cargo.chofer ?? 'el chofer'}.`}
      acciones={
        <>
          <Boton variante="texto" onClick={onCerrar}>
            Volver
          </Boton>
          <Boton
            variante="peligro"
            cargando={cancelar.isPending}
            disabled={motivo.trim() === ''}
            onClick={async () => {
              setError(null);
              try {
                await cancelar.mutateAsync({ id: cargo.id, data: { motivo } });
                toast.success('Cargo cancelado.');
                onCerrar();
              } catch (err) {
                setError(mensajeError(err));
              }
            }}
          >
            Cancelar cargo
          </Boton>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Campo etiqueta="Motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
      </div>
    </Dialogo>
  );
}

/** RN-LOG-26: alta manual de un cargo `DiferenciaPrecio` u `Otro` (este puede ser negativo para compensar). */
export function DialogoNuevoCargo({ liquidacionId, onCerrar }: { liquidacionId?: string; onCerrar: () => void }) {
  const crear = useCrearCargoChofer();
  const { data: vendedores } = useListarVendedores();
  const [tipo, setTipo] = useState<TipoCargoChofer>('DiferenciaPrecio');
  const [choferId, setChoferId] = useState('');
  const [fecha, setFecha] = useState(fechaOperativa());
  const [importe, setImporte] = useState('');
  const [notas, setNotas] = useState('');
  const [error, setError] = useState<string | null>(null);
  const valido = Number(importe) !== 0 && importe.trim() !== '' && (liquidacionId || choferId) && (tipo !== 'Otro' || notas.trim() !== '');
  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo="Nuevo cargo al chofer"
      descripcion="Diferencia de precio u otro concepto. Un cargo «Otro» negativo compensa un cargo ya aplicado."
      acciones={
        <>
          <Boton variante="texto" onClick={onCerrar}>
            Cancelar
          </Boton>
          <Boton
            cargando={crear.isPending}
            disabled={!valido}
            onClick={async () => {
              setError(null);
              try {
                await crear.mutateAsync({
                  data: {
                    tipo,
                    importe: Number(importe),
                    liquidacionId: liquidacionId ?? null,
                    choferId: liquidacionId ? null : choferId,
                    fechaOperativa: liquidacionId ? null : fecha,
                    notas: notas || null,
                  },
                });
                toast.success('Cargo registrado.');
                onCerrar();
              } catch (err) {
                setError(mensajeError(err));
              }
            }}
          >
            Guardar
          </Boton>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Selector
          etiqueta="Tipo"
          opciones={TIPOS_CARGO.filter((t) => t.valor === 'DiferenciaPrecio' || t.valor === 'Otro')}
          value={tipo}
          onChange={(e) => setTipo(e.target.value as TipoCargoChofer)}
        />
        {!liquidacionId && (
          <>
            <Selector
              etiqueta="Chofer"
              vacio="Elige el chofer"
              opciones={(vendedores ?? []).map((u) => ({ valor: u.id, etiqueta: u.nombreCompleto }))}
              value={choferId}
              onChange={(e) => setChoferId(e.target.value)}
            />
            <Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
          </>
        )}
        <Campo
          etiqueta="Importe"
          type="number"
          step="0.01"
          inputMode="decimal"
          prefijo="$"
          value={importe}
          onChange={(e) => setImporte(e.target.value)}
          ayuda="Positivo = se le cobra al chofer."
        />
        <Campo etiqueta={tipo === 'Otro' ? 'Nota' : 'Nota (opcional)'} value={notas} onChange={(e) => setNotas(e.target.value)} />
      </div>
    </Dialogo>
  );
}

/** Tabla de cargos con cancelación (cuenta de choferes, liquidación y corte). */
export function TablaCargos({ cargos, cargando, puedeCancelar }: { cargos: CargoChoferDto[]; cargando?: boolean; puedeCancelar: boolean }) {
  const [cancelando, setCancelando] = useState<CargoChoferDto | null>(null);
  const columnas = useMemo<ColumnDef<CargoChoferDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Chofer', accessorKey: 'chofer', cell: ({ row }) => row.original.chofer ?? '—', meta: { ocultarEnMovil: true } },
      { header: 'Tipo', accessorKey: 'tipo', cell: ({ row }) => etiquetaTipoCargo(row.original.tipo) },
      {
        header: 'Detalle',
        id: 'detalle',
        cell: ({ row }) => {
          const c = row.original;
          return (
            <span className="text-sm">
              {c.producto ? `${c.sku} · ${formatoCantidad(c.cantidadBase)} × ${formatoMoneda(c.precioUnitario)}` : ''}
              {c.liquidacionFolio ? <span className="block text-xs text-muted">{c.liquidacionFolio}</span> : null}
              {c.notas ? <span className="block text-xs text-muted">{c.notas}</span> : null}
            </span>
          );
        },
        meta: { ocultarEnMovil: true },
      },
      { header: 'Importe', accessorKey: 'importe', cell: ({ row }) => formatoMoneda(row.original.importe), meta: { alinear: 'fin' } },
      {
        header: 'Estado',
        accessorKey: 'estado',
        cell: ({ row }) => (
          <span title={row.original.motivoCancelacion ?? undefined}>
            <EtiquetaEstado estado={row.original.estado} />
          </span>
        ),
      },
      ...(puedeCancelar
        ? [
            {
              id: 'acciones',
              header: '',
              cell: ({ row }: { row: { original: CargoChoferDto } }) =>
                row.original.estado === 'Pendiente' ? (
                  <Boton variante="texto" tamano="chico" onClick={() => setCancelando(row.original)}>
                    Cancelar
                  </Boton>
                ) : null,
            } satisfies ColumnDef<CargoChoferDto, unknown>,
          ]
        : []),
    ],
    [puedeCancelar],
  );
  return (
    <>
      <TablaDatos columnas={columnas} datos={cargos} idFila={(c) => c.id} cargando={cargando} mensajeVacio="Sin cargos." />
      {cancelando && <DialogoCancelarCargo cargo={cancelando} onCerrar={() => setCancelando(null)} />}
    </>
  );
}

const ESTADOS: { valor: EstadoCargoChofer; etiqueta: string }[] = [
  { valor: 'Pendiente', etiqueta: 'Pendiente' },
  { valor: 'Aplicado', etiqueta: 'Aplicado' },
  { valor: 'Cancelado', etiqueta: 'Cancelado' },
];

/** RN-LOG-25 a 27: cuenta de cada chofer. */
export function CuentaChoferes() {
  const { tienePermiso } = useSesion();
  const hoy = fechaOperativa();
  const [choferId, setChoferId] = useState('');
  const [estado, setEstado] = useState<EstadoCargoChofer | ''>('Pendiente');
  const [desde, setDesde] = useState(sumarDias(hoy, -30));
  const [hasta, setHasta] = useState(hoy);
  const [nuevo, setNuevo] = useState(false);
  const { data: vendedores } = useListarVendedores();
  const { data, isLoading } = useListarCargosChofer({
    choferId: choferId || undefined,
    estado: estado || undefined,
    desde: desde || undefined,
    hasta: hasta || undefined,
  });
  const total = (data ?? []).filter((c) => c.estado !== 'Cancelado').reduce((s, c) => s + Math.round(c.importe * 100), 0) / 100;
  const puede = tienePermiso('logistica.cargos_chofer');
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Cuenta de choferes"
        subtitulo="Cargos por producto a cobro, faltantes y otros conceptos. Se descuentan en el corte semanal."
        migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Cuenta de choferes' }]}
        acciones={
          puede && (
            <Boton icono="add" onClick={() => setNuevo(true)}>
              Nuevo cargo
            </Boton>
          )
        }
      />
      <BarraFiltros>
        <Selector
          etiqueta="Chofer"
          vacio="Todos"
          opciones={(vendedores ?? []).map((u) => ({ valor: u.id, etiqueta: u.nombreCompleto }))}
          value={choferId}
          onChange={(e) => setChoferId(e.target.value)}
        />
        <Selector etiqueta="Estado" vacio="Todos" opciones={ESTADOS} value={estado} onChange={(e) => setEstado(e.target.value as EstadoCargoChofer | '')} />
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
      </BarraFiltros>
      <p className="m-0 mb-2 text-sm text-muted">Total (sin cancelados): {formatoMoneda(total)}</p>
      <TablaCargos cargos={data ?? []} cargando={isLoading} puedeCancelar={puede} />
      {nuevo && <DialogoNuevoCargo onCerrar={() => setNuevo(false)} />}
    </Pagina>
  );
}
