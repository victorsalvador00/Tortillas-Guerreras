# Módulo Facturación (FAC)

> **D-06 pendiente.** La integración real (agente CONTPAQi o API de facturación en la nube) no se
> decide hasta recibir información del cliente. Todo se programa contra `IProveedorFacturacion` con
> `ProveedorFacturacionSimulado`, que genera un UUID falso, un XML de ejemplo y un PDF.

## Casos de uso
1. Configurar datos del emisor y series.
2. Facturar una venta o pedido desde la web (clientes institucionales/autoservicio).
3. Autofactura: el cliente captura folio del ticket + datos fiscales en el portal público.
4. Generar factura global del periodo con las ventas de contado no facturadas.
5. Consultar, descargar (XML/PDF) y reenviar facturas por correo.
6. Cancelar factura `[SUP-15]` (motivo SAT).

## Proveedor

```csharp
public interface IProveedorFacturacion
{
    Task<ResultadoTimbrado> TimbrarAsync(FacturaParaTimbrar factura, CancellationToken ct);
    Task<ResultadoCancelacion> CancelarAsync(Guid uuidFiscal, string motivo, Guid? sustituye, CancellationToken ct);
}
```

Flujo: crear factura `Pendiente` → registro en `outbox` (`TimbrarFactura`) → job de Hangfire llama
al proveedor → `Timbrada` (guarda XML/PDF en Spaces) o `Error` con reintentos (1, 5, 15, 60 min;
máximo 10).

## Reglas
- **RN-FAC-01** Una venta solo puede estar en una factura vigente (`Ingreso` o `Global`).
- **RN-FAC-02** Autofactura: el folio debe existir, estar `Registrada`, no facturada, con forma de
  pago contado, y dentro del plazo `autofactura.limite` (por defecto: hasta el último día del mes de
  la venta) `[SUP-11]`.
- **RN-FAC-03** Validación de datos fiscales en portal: RFC con formato válido (12 o 13 caracteres),
  régimen y uso CFDI de los catálogos del SAT, código postal de 5 dígitos. La validación contra el
  SAT la hace el proveedor al timbrar.
- **RN-FAC-04** Factura global: incluye ventas `Registrada`, contado, no facturadas, del periodo;
  receptor `XAXX010101000`, "PUBLICO EN GENERAL", régimen 616, uso `S01`, CP del emisor; un
  concepto por venta (ticket) `[SUP-11]`.
- **RN-FAC-05** Una venta incluida en una global ya no se puede autofacturar.
- **RN-FAC-06** Conceptos usan `clave_prod_serv_sat` y la `clave_sat` de la unidad; un producto sin
  clave SAT bloquea la facturación con `FAC-006`.
- **RN-FAC-07** `metodo_pago_sat` = `PUE` para contado, `PPD` para crédito.
- **RN-FAC-08** El portal limita a 10 solicitudes por IP por hora y usa captcha simple `[SUP-15]`.
- **RN-FAC-09** Al timbrar se envía el XML y PDF al correo indicado.

## Endpoints
`/api/v1/facturacion` (autenticado): `/emisor`, `/facturas` (GET/POST),
`/facturas/{id}/reintentar`, `/facturas/{id}/cancelar`, `/facturas/{id}/xml`, `/pdf`,
`/factura-global/previsualizar?desde&hasta`, `POST /factura-global`.

`/api/v1/portal` (anónimo, con rate limiting): `GET /tickets/{folio}?total=` (verifica folio + total
del ticket para no exponer datos), `POST /solicitudes`, `GET /solicitudes/{id}` (estado y descarga).

## Criterios de aceptación
- Con el proveedor simulado, una solicitud válida de autofactura termina en `Timbrada` y el portal
  permite descargar XML y PDF.
- La previsualización de la global excluye las ventas autofacturadas del periodo.
