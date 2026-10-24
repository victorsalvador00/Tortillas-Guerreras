# Mapeo de la información del cliente (v0.2)

Fuentes recibidas el 24-oct-2026. **Los originales no se suben a este repositorio** porque es público y contienen
nombres de personal, clientes y montos; quedan en el proyecto de Claude "TortillasGuerreras". Aquí solo va la
estructura de cada formato y su mapeo al modelo.

Marcas: **igual** (existe igual), **otro nombre** (existe con otro nombre), **falta** (no existe; se agrega en
v0.2), **sobra** (existe en el sistema y el cliente no lo usa), **fuera** (no entra en v0.2).

## 1. `Códigos de Venta depurados.xlsx`

Dos hojas: "Códigos Vtas Utilizados" (118 códigos activos) y "Códigos no utilizados" (107).

| Columna del cliente | Modelo | Marca |
|---|---|---|
| Código del producto (`PV01`, `PV62`…) | `catalogos.productos.sku` | igual (RN-CAT-07) |
| Nombre del producto | `catalogos.productos.nombre` | igual |
| AUTO SERVICIO / DETALLE / INSTITUCIONAL (marcas por canal) | `ventas.productos_exclusivos` (cuando trae un cliente: SANBORNS, ABC) | falta → v0.2 |
| PUEBLA / MÉRIDA | — | fuera (P-07) |
| Hoja "no utilizados" | productos inactivos / no se cargan | igual (RN-CAT-07) |

**Inconsistencias a resolver con el cliente (P-06):**

1. Nombres repetidos con distinto código: `PV09`/`PV34` (TAQUERA), `PV103`/`PV112` (SALSA MEXICANA),
   `PV16A`/`PV16B` (HUARACHE ESPECIAL 14 X 27 PAQ 9 PZAS; uno marcado SANBORNS).
2. El formato de pedido de autoservicio usa códigos distintos a los del Excel: Sanopal como `PV121` (en el Excel
   `PV121` está en "no utilizados"; el activo es `PV62` TOSTADA SANOPAL), sope 6 pz como `PV90A` (no utilizado; el
   activo es `PV90`), FIT blanca como `PV112` y FIT amarilla como `PV119` (en el Excel son salsas).
3. Productos de los formatos que no se identifican en el Excel: FIT blanca, FIT amarilla, sopes charola beige,
   mini Comer, gomita, P. caramelo, botana, salsa macha, chicharrón 700, línea OCHOA y "O SABOR T MAÍZ".
4. Falta en el Excel: unidad base, paquetes por caja, tipo (producido o reventa), nombre corto, IVA/IEPS y si aplica
   producto a cobro — se piden para la plantilla de carga (T-8.14).

## 2. Estimado de producción — Detalle (por fecha)

| Elemento del formato | Modelo | Marca |
|---|---|---|
| Fecha | `ventas.pedidos.fecha_entrega` | igual |
| Filas R-51 … R-61 | `logistica.rutas.codigo` (`R51`…) con canal DET | otro nombre (sin guion, V2-11) |
| Columna "cajas" de tortilla y "paq x caja 24" | `productos_unidades` con `predeterminada_traslado` | falta → v0.2 (RN-CAT-09) |
| Columnas por producto (tortilla, sope, maíz pozolero, chicharrón, salsa macha, sabritón, botana, totopo, tostada, salsas, T-450, taq/logo, churritos, gomita, totopo frito) | partidas del pedido de ruta (RN-VEN-16); encabezado `nombre_corto` | falta → v0.2 |
| Observaciones ("4 harinillas, 1 norteña #30") | partidas de productos fuera de columnas + `pedidos_partidas.notas` | falta → v0.2 |
| TOTAL | renglón calculado (RN-PRO-11) | falta → v0.2 |
| REAL (vacío en papel) | lo cargado en envíos de la fecha | falta → v0.2 |
| "Recarga 30 kg del #14" (a mano) | `logistica.envios` tipo `Recarga` | igual |
| Leyenda "anotar cantidades en paquetes" | unidad de venta predeterminada | igual |

## 3. Estimado de producción — Autoservicio (por fecha)

| Elemento | Modelo | Marca |
|---|---|---|
| Ruta 1 … 14 y vendedor | `rutas.codigo` (`R1`…) y `rutas.vendedor_id` (chofer predeterminado) | igual |
| 17 columnas de producto (T-800, sopes, sope 6 pz, charola beige, mini nuevo, mini Comer, flautas, HNA, tostadas, totopos, taquera, T-450, sanopal, FIT blanca/amarilla, T-La Comer, huarache) | suma de pedidos por tienda de la ruta (RN-PRO-11) | falta → v0.2 |
| Renglones CAJAS / PQTS | desglose RN-CAT-09 | falta → v0.2 |

## 4. Producción – Almacén / Ventas institucional (por día)

Tabla dinámica de Excel productos × destino.

| Elemento | Modelo | Marca |
|---|---|---|
| Columnas R-101 … R-103, R-151, R-51 … | rutas INST, CEDIS y DET | otro nombre |
| `LCT-DOM`, `LCT-SAB` (Casa de Toño, domingo y sábado) | pedidos del cliente con `fecha_entrega` distinta (RN-VEN-15) | igual |
| `AUTO` | canal AUTO | igual |
| `AUT-FORA` | — | fuera (P-07) |
| Mezcla KG y paquetes | unidad de venta por producto | igual |

## 5. Pedido de autoservicio por tienda (por ruta y fecha)

| Elemento | Modelo | Marca |
|---|---|---|
| "Tiendas con pedidos para" fecha | `pedidos.fecha_entrega` | igual |
| Filas = tiendas (Walmart, Walmart Express, Superama, La Comer, City Market, Fresko, Chedraui) | `ventas.clientes` con `grupo_cliente_id` | falta (grupo) → v0.2 |
| Columnas = productos con su código `PV` | `pedidos_partidas` | igual |
| Lista de tiendas repetida abajo (cambios) | partidas `CambioFisico` (RN-VEN-17) | falta → v0.2 |
| Renglón CAMBIOS | Σ partidas `CambioFisico` | falta → v0.2 |
| Renglón DESCUENTOS | — | pendiente (P-04) |

## 6. Liquidación de ruta — Institucional / CEDIS

| Elemento | Modelo | Marca |
|---|---|---|
| Vendedor, ruta (CEDIS 151), fecha | `liquidaciones.chofer_id`, `ruta_id`, `fecha_operativa` | falta (chofer) → v0.2 |
| Producto, CARGA, SOBRANTE, VENTA, TOTAL | `liquidaciones_partidas` formato `Simplificado` | otro nombre (RN-LOG-19) |
| Cliente con folio en columna CRÉDITO o EFECTIVO | ventas del día por cliente (solo lectura) | igual |
| VENTA, + PAGOS, − CRÉDITOS, − GASTOS, − DIF. PRECIO, + DIF. PRECIOS, − CHEQUES, EFECTIVO | bloque de dinero (RN-LOG-08, 23) | falta (gastos, cheques, dif. precio) → v0.2 |
| Documentos extras entregados | `liquidaciones.notas` | otro nombre |
| Nombre y firma | etapas con usuario (RN-LOG-20) | falta → v0.2 |

## 7. Liquidación de ruta — Detalle

| Elemento | Modelo | Marca |
|---|---|---|
| CARGA, SOBRANTE, DEVOLUCIÓN, RECARGA, VENTA, TOTAL ($) | `cargado_inicial`, `sobrante_contado`, `devuelto_clientes_no_vendible`, `recargado`, `vendido`, `importe_venta` | otro nombre / falta (recarga separada) |
| Venta total | `liquidaciones.total_venta` | falta → v0.2 |
| CRÉDITOS (cliente, folio, cantidad) | ventas a crédito del día (solo lectura) | igual |
| DOCUMENTOS PAGADOS / PAGO DE CRÉDITOS | cobros del día (solo lectura; cobranza de clientes fuera de v0.2) | igual |
| GASTOS | `liquidaciones_gastos` | falta → v0.2 |
| Transferencia (a mano) | ventas/cobros con forma de pago `Transferencia` | igual |
| EFECTIVO DETALLE | `efectivo_entregado` | igual |
| PRODUCTO A COBRO (por producto) | `liquidaciones_partidas.cantidad_a_cobro` → `cargos_chofer` | falta → v0.2 |
| Firmas: sobrante y dev. almacén / entrega vendedor / revisa supervisor / recibe liquidaciones | etapas `ProductoConfirmado` → `Revisada` → `Cerrada` | falta → v0.2 |

## Sobra (existe en el sistema y no aparece en la evidencia)

- Conteos físicos y valuación (ocultos en el menú desde `38d3de4`): se conservan.
- Factura global y portal de autofactura: sin cambios (fuera de v0.2).
