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

## Decisiones de implementación (T-6.1 a T-6.3)
- **Proveedor:** `Facturacion:Proveedor` = `Simulado` (único disponible hasta D-06). Genera UUID, XML con la estructura
  CFDI 4.0 (sello y certificado `SIMULADO`) y PDF. El PDF se escribe directo (Helvetica estándar y QR dibujado con la
  matriz de QRCoder) para no depender de fuentes instaladas en el servidor; no se usa PdfSharp.
- **Archivos:** `Archivos:Proveedor` = `Local` (por defecto, `Archivos:Ruta` o `<ContentRoot>/App_Data/archivos`) o
  `Spaces` (`Spaces:Key`, `Secret`, `Bucket`, `Endpoint`). Llave `facturas/AAAA/MM/<serie><folio>-<uuid>.xml|pdf`.
- **Correo (RN-FAC-09):** con `Smtp:Host` (`Port`, `Usuario`, `Contrasena`, `Remitente`, `Ssl`) se envía por SMTP; sin él
  solo se registra en el log. Un fallo de correo no revierte el timbrado.
- **Folios:** número consecutivo por tipo (`facturacion.folio_ingreso_seq`, `facturacion.folio_global_seq`); la serie
  sale del emisor.
- **Outbox:** Hangfire ejecuta `ProcesadorOutboxFacturacion` cada minuto; cada mensaje en su transacción con
  `FOR UPDATE SKIP LOCKED`. Reintentos a 1, 5, 15 y 60 min (luego cada 60), máximo 10; agotados, la solicitud de
  autofactura pasa a `Error`. `POST /facturas/{id}/reintentar` reinicia el contador.
- **Factura global:** un concepto por ticket (clave `01010101`, unidad `ACT`, cantidad 1, `NoIdentificacion` = folio);
  si un ticket mezcla tasas de IVA/IEPS se parte en un concepto por tasa con el mismo folio. Periodicidad del parámetro
  `factura_global.periodicidad` (Mensual → `04`); `Meses` y `Año` de la fecha final del periodo.
- **Portal:** el límite por IP (RN-FAC-08) aplica a `GET /tickets/{folio}` y `POST /solicitudes`; captcha, estado y
  descargas no cuentan. Los rechazos se guardan como solicitud `Rechazada` con su motivo. Captcha aritmético en memoria
  de un solo uso (10 min) [SUP-15].
- **Cancelación [SUP-15]:** motivo SAT `01`–`04` (`01` exige `sustituye`); si estaba timbrada se cancela con el
  proveedor; las ventas quedan libres para volver a facturarse.
- **Factura web:** las ventas deben ser del mismo cliente; el receptor sale de los datos fiscales del cliente o del
  cuerpo de la solicitud. Ventas a crédito → `PPD`/`99`; contado → `PUE` y `01` (o `03` si todas fueron transferencia).
