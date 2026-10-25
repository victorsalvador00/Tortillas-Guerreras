import { useParams } from '@tanstack/react-router';
import {
  listarClientes,
  mensajeError,
  useActualizarClientesRuta,
  useListarClientesRuta,
  useObtenerRuta,
  type ClienteRutaDto,
} from '@tg/api-client';
import { Aviso, BarraAccion, Boton, BotonIcono, BuscadorSeleccion, Dato, Datos, EtiquetaEstado, Tarjeta, cn, toast, type OpcionBusqueda } from '@tg/ui';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { DIAS_SEMANA } from './comunes-logistica';
import { DialogoRuta } from './Configuracion';

interface FilaCliente {
  clienteId: string;
  codigo: string;
  nombre: string;
  esGenerico: boolean;
  diasVisita: number[];
}

/** Reordena una lista moviendo el elemento `i` una posición (−1 sube, +1 baja). */
export function mover<T>(lista: T[], i: number, delta: -1 | 1): T[] {
  const j = i + delta;
  if (j < 0 || j >= lista.length) return lista;
  const copia = [...lista];
  [copia[i], copia[j]] = [copia[j], copia[i]];
  return copia;
}

function aFilas(clientes: ClienteRutaDto[]): FilaCliente[] {
  return [...clientes]
    .sort((a, b) => a.ordenVisita - b.ordenVisita)
    .map((c) => ({ clienteId: c.clienteId, codigo: c.codigo, nombre: c.nombre, esGenerico: c.esGenerico, diasVisita: [...c.diasVisita] }));
}

export function RutaDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const ruta = useObtenerRuta(id);
  const clientes = useListarClientesRuta(id);
  const guardar = useActualizarClientesRuta();
  const [filas, setFilas] = useState<FilaCliente[]>([]);
  const [modificado, setModificado] = useState(false);
  const [editar, setEditar] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (clientes.data && !modificado) setFilas(aFilas(clientes.data));
  }, [clientes.data, modificado]);

  const cambiar = (nuevas: FilaCliente[]) => {
    setFilas(nuevas);
    setModificado(true);
  };

  const buscarClientes = useCallback(
    async (texto: string): Promise<OpcionBusqueda[]> => {
      const r = await listarClientes({ buscar: texto || undefined, activo: true, esGenerico: false, tamano: 30 });
      return r.items.map((c) => ({ valor: `${c.id}|${c.codigo}|${c.nombre}`, etiqueta: `${c.codigo} · ${c.nombre}`, detalle: c.canal }));
    },
    [],
  );

  const ids = useMemo(() => new Set(filas.map((f) => f.clienteId)), [filas]);

  const enviar = async () => {
    setError(null);
    try {
      await guardar.mutateAsync({
        id,
        data: { clientes: filas.map((f, i) => ({ clienteId: f.clienteId, ordenVisita: i + 1, diasVisita: f.diasVisita })) },
      });
      setModificado(false);
      toast.success('Clientes de la ruta guardados. Bajan a la app en la siguiente sincronización.');
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  const r = ruta.data;
  return (
    <Pagina>
      <EstadoConsulta cargando={ruta.isLoading} error={ruta.error}>
        {r && (
          <>
            <EncabezadoPagina
              titulo={`Ruta ${r.codigo} · ${r.nombre}`}
              subtitulo="Clientes en orden de visita y días en que se visitan."
              migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Rutas', a: '/logistica/rutas' }, { etiqueta: r.codigo }]}
              acciones={
                <Boton variante="secundario" icono="edit" onClick={() => setEditar(true)}>
                  Editar ruta
                </Boton>
              }
            />
            <Tarjeta className="mb-4">
              <Datos>
                <Dato etiqueta="Vendedor">{r.vendedorNombre ?? 'Sin asignar'}</Dato>
                <Dato etiqueta="Vehículo">{r.vehiculoPlacas ?? 'Sin asignar'}</Dato>
                <Dato etiqueta="Almacén de ruta">{r.almacenCodigo}</Dato>
                <Dato etiqueta="Estado">
                  <EtiquetaEstado estado={r.activo ? 'Activo' : 'Inactivo'} />
                </Dato>
              </Datos>
            </Tarjeta>
            <Tarjeta
              titulo={`Clientes (${filas.length})`}
              subtitulo="Usa las flechas para cambiar el orden de visita. Días: L, M, X, J, V, S, D."
              acciones={
                <BuscadorSeleccion
                  etiqueta="Agregar cliente"
                  compacto
                  className="w-72"
                  valor={null}
                  buscar={buscarClientes}
                  onCambiar={(valor) => {
                    if (!valor) return;
                    const [clienteId, codigo, ...nombre] = valor.split('|');
                    if (ids.has(clienteId)) {
                      toast.info('El cliente ya está en la ruta.');
                      return;
                    }
                    cambiar([...filas, { clienteId, codigo, nombre: nombre.join('|'), esGenerico: false, diasVisita: [1, 2, 3, 4, 5, 6] }]);
                  }}
                />
              }
            >
              {error && <Aviso tipo="error" className="mb-3">{error}</Aviso>}
              <EstadoConsulta cargando={clientes.isLoading} error={clientes.error}>
                {filas.length === 0 && <p className="m-0 text-sm text-muted">La ruta no tiene clientes.</p>}
                <ol className="m-0 flex list-none flex-col gap-2 p-0">
                  {filas.map((f, i) => (
                    <li key={f.clienteId} className="flex flex-wrap items-center gap-3 rounded-xl border border-row-divider px-3 py-2">
                      <span className="grid size-8 place-items-center rounded-full bg-soft text-sm font-medium text-primary">{i + 1}</span>
                      <div className="min-w-48 flex-1">
                        <span className="block font-medium">{f.nombre}</span>
                        <span className="text-xs text-muted">
                          {f.codigo}
                          {f.esGenerico ? ' · Cliente genérico' : ''}
                        </span>
                      </div>
                      <div className="flex gap-1" role="group" aria-label={`Días de visita de ${f.nombre}`}>
                        {DIAS_SEMANA.map((d) => {
                          const activo = f.diasVisita.includes(d.valor);
                          return (
                            <button
                              key={d.valor}
                              type="button"
                              title={d.nombre}
                              aria-pressed={activo}
                              onClick={() =>
                                cambiar(
                                  filas.map((x, j) =>
                                    j === i
                                      ? { ...x, diasVisita: activo ? x.diasVisita.filter((v) => v !== d.valor) : [...x.diasVisita, d.valor].sort() }
                                      : x,
                                  ),
                                )
                              }
                              className={cn(
                                'size-8 rounded-full text-xs font-medium transition-colors',
                                activo ? 'bg-primary text-on-primary' : 'bg-soft text-muted hover:text-on-surface',
                              )}
                            >
                              {d.corto}
                            </button>
                          );
                        })}
                      </div>
                      <div className="flex gap-1">
                        <BotonIcono icono="arrow_upward" etiqueta="Subir" tamano="chico" disabled={i === 0} onClick={() => cambiar(mover(filas, i, -1))} />
                        <BotonIcono
                          icono="arrow_downward"
                          etiqueta="Bajar"
                          tamano="chico"
                          disabled={i === filas.length - 1}
                          onClick={() => cambiar(mover(filas, i, 1))}
                        />
                        <BotonIcono
                          icono="delete"
                          etiqueta="Quitar de la ruta"
                          tamano="chico"
                          disabled={f.esGenerico}
                          onClick={() => cambiar(filas.filter((_, j) => j !== i))}
                        />
                      </div>
                    </li>
                  ))}
                </ol>
              </EstadoConsulta>
            </Tarjeta>
            {modificado && (
              <BarraAccion resumen="Hay cambios sin guardar en los clientes de la ruta.">
                <Boton
                  variante="texto"
                  onClick={() => {
                    setModificado(false);
                    if (clientes.data) setFilas(aFilas(clientes.data));
                  }}
                >
                  Descartar
                </Boton>
                <Boton cargando={guardar.isPending} onClick={enviar}>
                  Guardar clientes
                </Boton>
              </BarraAccion>
            )}
            {editar && <DialogoRuta ruta={r} abierto onCambiar={setEditar} />}
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
