import 'package:flutter_test/flutter_test.dart';
import 'package:unitatlas_capture/scan/scan_parser.dart';

void main() {
  test('parses UnitAtlas, GS1 DataMatrix, GS1-128, EAN and SSCC', () {
    expect(ScanParser.parse('unitatlas:unit:UA-1').identifier, 'UA-1');
    final matrix = ScanParser.parse(']d2010487123456789021ABC001');
    expect(matrix.symbology, 'GS1_DATA_MATRIX');
    expect(matrix.fields['gtin'], '04871234567890');
    expect(matrix.fields['serial'], 'ABC001');
    expect(
      ScanParser.parse(']C100123456789012345675').fields['sscc'],
      '123456789012345675',
    );
    expect(ScanParser.parse('1234567890128').symbology, 'EAN');
    expect(ScanParser.parse('123456789012345675').symbology, 'SSCC');
  });

  test('parses lot separator and plain DataMatrix', () {
    final parsed = ScanParser.parse(']d2010487123456789010LOT-1\u001d21SER-1');
    expect(parsed.fields['lot'], 'LOT-1');
    expect(parsed.fields['serial'], 'SER-1');
    expect(ScanParser.parse('plain-code').symbology, 'DATA_MATRIX');
  });
}
