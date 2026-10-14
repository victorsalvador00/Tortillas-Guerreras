import { Image, View } from 'react-native';
import logo from '../../assets/marca/logo-guerreras.png';

/** Logotipo del cliente (Guerreras, por Familia Ochoa) sobre placa blanca redondeada, como en la web. */
export function Logo({ ancho = 220 }: { ancho?: number }) {
  return (
    <View style={{ backgroundColor: '#ffffff', borderRadius: 12, paddingHorizontal: 10, paddingVertical: 6, alignSelf: 'center' }}>
      <Image source={logo} style={{ width: ancho, height: (ancho * 196) / 720 }} resizeMode="contain" accessibilityLabel="Guerreras, por Familia Ochoa" />
    </View>
  );
}
