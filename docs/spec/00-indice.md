# 00 — Índice y contexto

**Estado:** versión 0.2 (24-oct-2026) — ver `11-cambios-v0.2.md`. Versión 0.1 (13-oct-2026). Modelo de datos y reglas **por defecto** para una operación
típica de tortillería industrial con distribución en ruta. Se complementa cuando el cliente entregue
su flujo real y sus formatos de Excel (ver `10-supuestos-y-pendientes.md`).

## Contexto del negocio

- Empresa que produce productos de maíz (masa, tortilla y derivados). Solo producen a partir de maíz;
  el resto de los productos que venden se compran a terceros (reventa).
- Hoy todo es manual (cuadernos y Excel): no hay órdenes de producción, conteos ni registro de mermas.
- Tuvieron una app móvil de distribución con otro proveedor y la cancelaron: los datos no cuadraban,
  se producía de más, no funcionaba sin señal y dependía de actualizaciones constantes.
- Canales de venta **(v0.2)**: **Autoservicio** (corporativos: Walmart, Chedraui, La Comer, City Market; pedido
  por tienda que levanta el vendedor), **Detalle** (tienditas; pedido global por ruta que hace el supervisor),
  **Institucional** (restaurantes, taquerías) y **CEDIS** (entregas a centros de distribución); los dos últimos piden
  por teléfono a la oficina. Rutas por canal: `R1`–`R14`, `R51`…, `R101`…, `R151`….
- Chofer, camión y ruta no tienen relación fija; se eligen en cada carga.
- Almacén liquida cada ruta diario (carga, recarga, venta, sobrante reutilizable, devolución = merma, cambios
  físicos) y le carga al chofer el producto devuelto fuera de tolerancia ("producto a cobro"), que se descuenta en
  el corte semanal de comisiones.
- Objetivo central: saber con certeza cuánto entró, cuánto se produjo, cuánto salió y cuánto se vendió.
- Producción es el "cerebro": todo lo producido entra al almacén y el almacén distribuye.

## Documentos

| Archivo | Contenido |
|---|---|
| `01-arquitectura.md` | Decisiones de stack, módulos, dependencias entre módulos, transversales |
| `02-convenciones.md` | Convenciones de código, base de datos, API, pruebas y librerías aprobadas |
| `03-modelo-datos.md` | Modelo de datos por esquema y datos semilla |
| `04-modulos/*.md` | Casos de uso, estados, reglas de negocio (RN), endpoints y criterios por módulo |
| `05-sincronizacion-offline.md` | Protocolo de sincronización de la app de ruta |
| `06-app-ruta.md` | Pantallas, base local y flujos de la app Expo |
| `07-web.md` | Pantallas del panel administrativo y del portal de autofactura |
| `08-infra-ci.md` | Docker, Caddy, ambientes, GitHub Actions, respaldos |
| `09-plan-implementacion.md` | Fases y tareas ordenadas con criterios de aceptación |
| `10-supuestos-y-pendientes.md` | Registro de supuestos `[SUP-xx]` y decisiones pendientes |
| `11-cambios-v0.2.md` | Alcance, decisiones y trazabilidad de la versión 0.2 (reunión del 24-oct) |

## Convenciones de este spec

- `RN-<MOD>-nn`: regla de negocio. Cada una debe tener prueba.
- `[SUP-xx]`: supuesto no confirmado por el cliente. Valor por defecto configurable.
- `D-nn`: decisión de arquitectura (ver `01-arquitectura.md`).
- Prefijos de módulo: SEG, CAT, COM, PRO, INV, LOG, VEN, FAC, SYN.

## Flujo general

```text
Compras ──(recepción)──▶ Inventario MP ──(consumo por receta)──▶ Producción
                                                                    │
                                                    (entrada producto terminado)
                                                                    ▼
Pedidos (app/web) ─▶ Estimado ─▶ Plan sugerido ─▶ Producción          (v0.2)
        │
        ▼
Pedidos (web) ──▶ Logística: envío/carga ──▶ Almacén de ruta ──▶ Venta en ruta (app offline)
                                                  ▲                     │
                                  Retornos/sobrantes ◀── Liquidación ◀──┘
                                                                        │
                                              Ticket con folio ──▶ Autofactura / Factura global
                                  Liquidación ──▶ Cargos al chofer ──▶ Corte semanal (v0.2)
```
