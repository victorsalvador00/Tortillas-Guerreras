import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { esErrorApi, mensajeError } from '@tg/api-client';
import { BotonIcono, Girador, Icono, Logo, MenuAcciones, OPCIONES_TEMA, Plexus, useTema } from '@tg/ui';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { useSesion } from '@/app/sesion';

const esquema = z.object({
  usuario: z.string().trim().min(1, 'Escribe tu usuario.'),
  contrasena: z.string().min(1, 'Escribe tu contraseña.'),
});

type Datos = z.infer<typeof esquema>;

/**
 * Inicio de sesión: red de partículas de fondo, bienvenida a la izquierda y tarjeta translúcida a la derecha.
 * Tema claro en morados; oscuro en azul neón (mismo diseño que el SGO).
 */
export function Login() {
  const { iniciar } = useSesion();
  const { oscuro, modo, fijar } = useTema();
  const navegar = useNavigate();
  const { redirigir, motivo } = useSearch({ strict: false }) as { redirigir?: string; motivo?: string };
  const [ocultar, setOcultar] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Datos>({ resolver: zodResolver(esquema) });

  const enviar = handleSubmit(async (datos) => {
    setError(null);
    try {
      await iniciar(datos.usuario, datos.contrasena);
      await navegar({ to: redirigir && redirigir.startsWith('/') ? redirigir : '/' });
    } catch (e) {
      setError(esErrorApi(e) && e.codigo === 'SEG-002' ? e.message : mensajeError(e));
    }
  });

  return (
    <main
      className="relative grid min-h-full items-center justify-center gap-[clamp(2rem,6vw,96px)] overflow-hidden px-4 py-8 text-white sm:px-6 min-[800px]:grid-cols-[minmax(0,560px)_minmax(0,480px)] dark:text-[#90befe]"
      style={{
        background: oscuro
          ? 'radial-gradient(ellipse at 85% 70%, rgb(40 52 230 / 55%), transparent 55%), linear-gradient(160deg, #02040f 0%, #050a2a 55%, #0b1250 100%)'
          : 'radial-gradient(ellipse at 85% 70%, rgb(214 64 190 / 45%), transparent 55%), linear-gradient(160deg, #3d12a0 0%, #5318a6 55%, #9c218b 100%)',
      }}
    >
      <Plexus oscuro={oscuro} />

      <div className="absolute top-2 right-2 z-10">
        <MenuAcciones
          disparador={<BotonIcono icono={oscuro ? 'dark_mode' : 'light_mode'} etiqueta="Cambiar tema" plano className="text-inherit" />}
          opciones={OPCIONES_TEMA.map((t) => ({
            etiqueta: t.etiqueta,
            icono: t.icono,
            marcada: modo === t.modo,
            alSeleccionar: () => fijar(t.modo),
          }))}
        />
      </div>

      <header className="relative text-center min-[800px]:text-left">
        <div className="mb-6 inline-block">
          <Logo className="h-auto w-[clamp(240px,32vw,420px)] drop-shadow-[0_16px_24px_rgb(0_0_0/40%)]" />
        </div>
        <h1 className="m-0 text-[clamp(3rem,8vw,6rem)] leading-[1.05] font-bold tracking-[0.04em]">Bienvenido</h1>
        <p className="m-0 mt-2 text-[clamp(1.05rem,2.2vw,1.6rem)] tracking-[0.16em] text-white/85 dark:text-[#6f9dff]">
          Sistema de Gestión
        </p>
      </header>

      <section
        aria-labelledby="titulo-login"
        className="relative rounded-lg border border-white/12 bg-[rgb(68_10_112/82%)] px-6 py-8 shadow-[0_20px_50px_rgb(25_0_60/55%)] backdrop-blur-sm sm:px-10 sm:py-12 dark:border-[#2a63ff] dark:bg-[rgb(10_23_51/85%)] dark:shadow-[0_20px_50px_rgb(42_99_255/35%)]"
      >
        <h2 id="titulo-login" className="m-0 mb-6 flex items-center gap-2 text-2xl font-normal">
          <Icono nombre="storefront" /> Iniciar sesión
        </h2>

        {motivo === 'contrasena' && (
          <p role="status" className="mb-4 rounded-lg bg-[var(--tg-status-blue-bg)] px-4 py-3 text-[var(--tg-status-blue-fg)]">
            Tu contraseña se cambió. Inicia sesión con la nueva contraseña.
          </p>
        )}
        {motivo === 'expirada' && (
          <p role="status" className="mb-4 rounded-lg bg-[var(--tg-status-yellow-bg)] px-4 py-3 text-[var(--tg-status-yellow-fg)]">
            Tu sesión terminó. Inicia sesión de nuevo.
          </p>
        )}
        {error && (
          <p role="alert" className="mb-4 rounded-lg bg-[var(--tg-status-red-bg)] px-4 py-3 text-[var(--tg-status-red-fg)]">
            {error}
          </p>
        )}

        <form onSubmit={enviar} noValidate className="grid gap-4">
          <div className="grid gap-2">
            <label htmlFor="login-usuario" className="font-medium">
              Usuario
            </label>
            <input
              id="login-usuario"
              autoComplete="username"
              autoCapitalize="none"
              aria-invalid={errors.usuario ? true : undefined}
              aria-describedby={errors.usuario ? 'login-usuario-error' : undefined}
              className="h-[54px] w-full rounded-[27px] border border-white/30 bg-white/16 px-4 text-login text-white outline-none focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-white aria-[invalid=true]:border-[#ffd1dc] dark:border-[#2a63ff] dark:bg-[rgb(42_99_255/12%)] dark:text-[#cfe2ff]"
              {...register('usuario')}
            />
            {errors.usuario && (
              <p id="login-usuario-error" className="m-0 text-xs text-[#ffd1dc] dark:text-[#f4a3a3]">
                {errors.usuario.message}
              </p>
            )}
          </div>

          <div className="grid gap-2">
            <label htmlFor="login-contrasena" className="font-medium">
              Contraseña
            </label>
            <div className="relative">
              <input
                id="login-contrasena"
                type={ocultar ? 'password' : 'text'}
                autoComplete="current-password"
                aria-invalid={errors.contrasena ? true : undefined}
                className="h-[54px] w-full rounded-[27px] border border-white/30 bg-white/16 pr-[52px] pl-4 text-login text-white outline-none focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-white aria-[invalid=true]:border-[#ffd1dc] dark:border-[#2a63ff] dark:bg-[rgb(42_99_255/12%)] dark:text-[#cfe2ff]"
                {...register('contrasena')}
              />
              <BotonIcono
                icono={ocultar ? 'visibility' : 'visibility_off'}
                etiqueta={ocultar ? 'Mostrar contraseña' : 'Ocultar contraseña'}
                plano
                className="absolute top-1/2 right-0.5 -translate-y-1/2 text-white dark:text-[#cfe2ff]"
                onClick={() => setOcultar((o) => !o)}
              />
            </div>
            {errors.contrasena && <p className="m-0 text-xs text-[#ffd1dc] dark:text-[#f4a3a3]">{errors.contrasena.message}</p>}
          </div>

          <button
            type="submit"
            disabled={isSubmitting}
            className="mt-2 grid h-[54px] cursor-pointer place-items-center rounded-[27px] border-0 bg-[linear-gradient(90deg,#9b3cc9,#5a0a85)] text-base font-semibold tracking-[0.06em] text-white disabled:cursor-progress disabled:opacity-80 dark:bg-[linear-gradient(90deg,#19d3ff,#3d7bff)] dark:text-[#001233]"
          >
            {isSubmitting ? <Girador etiqueta="Iniciando sesión" /> : 'Iniciar sesión'}
          </button>
        </form>
      </section>
    </main>
  );
}
