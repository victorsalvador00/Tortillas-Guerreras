import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { cambiarContrasena, esErrorApi, mensajeError } from '@tg/api-client';
import { Aviso, Boton, Campo, Tarjeta } from '@tg/ui';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';

const esquema = z
  .object({
    contrasenaActual: z.string().min(1, 'Escribe tu contraseña actual.'),
    contrasenaNueva: z
      .string()
      .min(8, 'Debe tener al menos 8 caracteres.')
      .regex(/\d/, 'Debe incluir al menos un número.'),
    confirmacion: z.string(),
  })
  .refine((d) => d.contrasenaNueva === d.confirmacion, { path: ['confirmacion'], message: 'No coincide con la nueva.' });

type Datos = z.infer<typeof esquema>;

export function Perfil() {
  const { perfil, cerrar } = useSesion();
  const navegar = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    setError: setErrorCampo,
    formState: { errors, isSubmitting },
  } = useForm<Datos>({ resolver: zodResolver(esquema) });

  const enviar = handleSubmit(async (d) => {
    setError(null);
    try {
      await cambiarContrasena({ contrasenaActual: d.contrasenaActual, contrasenaNueva: d.contrasenaNueva });
      await cerrar();
      await navegar({ to: '/login', search: { motivo: 'contrasena' } });
    } catch (e) {
      if (esErrorApi(e) && e.errorDe('contrasenaActual')) {
        setErrorCampo('contrasenaActual', { message: e.errorDe('contrasenaActual') });
      } else {
        setError(mensajeError(e));
      }
    }
  });

  return (
    <Pagina ancho="medio">
      <EncabezadoPagina
        titulo="Cambiar contraseña"
        subtitulo={`${perfil?.nombreCompleto} · ${perfil?.usuario}`}
        migas={[{ etiqueta: 'Mi cuenta' }, { etiqueta: 'Cambiar contraseña' }]}
      />
      <Tarjeta>
        <form onSubmit={enviar} className="grid max-w-md gap-4" noValidate>
          {error && <Aviso tipo="error">{error}</Aviso>}
          <Campo etiqueta="Contraseña actual" type="password" autoComplete="current-password" error={errors.contrasenaActual?.message} {...register('contrasenaActual')} />
          <Campo
            etiqueta="Contraseña nueva"
            type="password"
            autoComplete="new-password"
            ayuda="Mínimo 8 caracteres con al menos un número."
            error={errors.contrasenaNueva?.message}
            {...register('contrasenaNueva')}
          />
          <Campo etiqueta="Confirma la contraseña nueva" type="password" autoComplete="new-password" error={errors.confirmacion?.message} {...register('confirmacion')} />
          <p className="m-0 text-sm text-muted">Al cambiarla se cierran tus sesiones abiertas y vuelves a iniciar sesión.</p>
          <div>
            <Boton type="submit" cargando={isSubmitting}>
              Cambiar contraseña
            </Boton>
          </div>
        </form>
      </Tarjeta>
    </Pagina>
  );
}
