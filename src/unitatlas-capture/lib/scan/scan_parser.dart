class ScanResult {
  const ScanResult(this.symbology, this.raw, this.fields);
  final String symbology;
  final String raw;
  final Map<String, String> fields;

  String get identifier =>
      fields['unitAtlas'] ??
      fields['sscc'] ??
      fields['serial'] ??
      fields['value'] ??
      raw;
}

class ScanParser {
  static ScanResult parse(String input) {
    final raw = input.trim();
    if (raw.startsWith('unitatlas:unit:')) {
      return ScanResult('UNITATLAS_QR', raw, {'unitAtlas': raw.substring(15)});
    }
    if (raw.startsWith('unitatlas:logistic_unit:')) {
      return ScanResult('UNITATLAS_QR', raw, {'value': raw.substring(24)});
    }
    if (raw.startsWith('UA-KZ-')) {
      return ScanResult('UNITATLAS_ID', raw, {'unitAtlas': raw});
    }

    final aim = raw.length >= 3 && raw.startsWith(']')
        ? raw.substring(0, 3)
        : null;
    final value = aim == null ? raw : raw.substring(3);
    if (aim == ']d2' || aim == ']C1') {
      return ScanResult(
        aim == ']d2' ? 'GS1_DATA_MATRIX' : 'GS1_128',
        raw,
        _parseGs1(value),
      );
    }
    if (aim == ']d1') return ScanResult('DATA_MATRIX', raw, {'value': value});
    if (aim == ']E0') return ScanResult('EAN', raw, {'value': value});
    if (_validCheckDigit(value, 18)) {
      return ScanResult('SSCC', raw, {'sscc': value});
    }
    if ((value.length == 8 || value.length == 13) &&
        value.codeUnits.every(_digit)) {
      return ScanResult('EAN', raw, {'value': value});
    }
    return ScanResult('DATA_MATRIX', raw, {'value': value});
  }

  static Map<String, String> _parseGs1(String value) {
    final fields = <String, String>{};
    var index = 0;
    while (index + 2 <= value.length) {
      final ai = value.substring(index, index + 2);
      index += 2;
      final fixed = {'00': 18, '01': 14, '11': 6, '17': 6}[ai];
      late String data;
      if (fixed != null) {
        if (index + fixed > value.length) break;
        data = value.substring(index, index + fixed);
        index += fixed;
      } else if (ai == '10' || ai == '21') {
        var end = value.indexOf('\u001d', index);
        if (end < 0 && ai == '10') end = value.indexOf('21', index + 1);
        if (end < 0) end = value.length;
        data = value.substring(index, end);
        index = end < value.length && value.codeUnitAt(end) == 29
            ? end + 1
            : end;
      } else {
        break;
      }
      final name = {
        '00': 'sscc',
        '01': 'gtin',
        '10': 'lot',
        '11': 'productionDate',
        '17': 'expiry',
        '21': 'serial',
      }[ai];
      if (name != null) fields[name] = data;
    }
    return fields;
  }

  static bool _validCheckDigit(String value, int length) {
    if (value.length != length || !value.codeUnits.every(_digit)) return false;
    var sum = 0;
    for (var index = 0; index < value.length - 1; index++) {
      sum += (value.codeUnitAt(index) - 48) * (index.isEven ? 3 : 1);
    }
    return (10 - sum % 10) % 10 == value.codeUnitAt(value.length - 1) - 48;
  }

  static bool _digit(int code) => code >= 48 && code <= 57;
}
