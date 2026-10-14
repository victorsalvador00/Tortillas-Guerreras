/** Catálogos del SAT (CFDI 4.0) usados en los datos fiscales del cliente y en el portal de autofactura. */

export const REGIMENES_FISCALES: { clave: string; nombre: string }[] = [
  { clave: '601', nombre: 'General de Ley Personas Morales' },
  { clave: '603', nombre: 'Personas Morales con Fines no Lucrativos' },
  { clave: '605', nombre: 'Sueldos y Salarios e Ingresos Asimilados a Salarios' },
  { clave: '606', nombre: 'Arrendamiento' },
  { clave: '607', nombre: 'Régimen de Enajenación o Adquisición de Bienes' },
  { clave: '608', nombre: 'Demás ingresos' },
  { clave: '610', nombre: 'Residentes en el Extranjero sin Establecimiento Permanente en México' },
  { clave: '611', nombre: 'Ingresos por Dividendos (socios y accionistas)' },
  { clave: '612', nombre: 'Personas Físicas con Actividades Empresariales y Profesionales' },
  { clave: '614', nombre: 'Ingresos por intereses' },
  { clave: '615', nombre: 'Régimen de los ingresos por obtención de premios' },
  { clave: '616', nombre: 'Sin obligaciones fiscales' },
  { clave: '620', nombre: 'Sociedades Cooperativas de Producción que optan por diferir sus ingresos' },
  { clave: '621', nombre: 'Incorporación Fiscal' },
  { clave: '622', nombre: 'Actividades Agrícolas, Ganaderas, Silvícolas y Pesqueras' },
  { clave: '623', nombre: 'Opcional para Grupos de Sociedades' },
  { clave: '624', nombre: 'Coordinados' },
  { clave: '625', nombre: 'Régimen de las Actividades Empresariales con ingresos a través de Plataformas Tecnológicas' },
  { clave: '626', nombre: 'Régimen Simplificado de Confianza' },
];

export const USOS_CFDI: { clave: string; nombre: string }[] = [
  { clave: 'G01', nombre: 'Adquisición de mercancías' },
  { clave: 'G02', nombre: 'Devoluciones, descuentos o bonificaciones' },
  { clave: 'G03', nombre: 'Gastos en general' },
  { clave: 'I01', nombre: 'Construcciones' },
  { clave: 'I08', nombre: 'Otra maquinaria y equipo' },
  { clave: 'D01', nombre: 'Honorarios médicos, dentales y gastos hospitalarios' },
  { clave: 'S01', nombre: 'Sin efectos fiscales' },
  { clave: 'CP01', nombre: 'Pagos' },
];

export const FORMAS_PAGO_SAT: Record<string, string> = {
  '01': 'Efectivo',
  '03': 'Transferencia electrónica de fondos',
  '99': 'Por definir',
};
