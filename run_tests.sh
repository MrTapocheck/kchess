#!/bin/bash
echo "=== Запуск набора тестов kchess ==="
for file in tests/*.txt; do
    echo ""
    echo "----------------------------------------"
    echo "ТЕСТ: $file"
    echo "----------------------------------------"
    dotnet run -- --test "$file"
done
echo ""
echo "=== Все тесты завершены ==="
