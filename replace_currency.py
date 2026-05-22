import os
import re

paths = []
for root, dirs, files in os.walk(r'c:\Users\acer\source\repos\FinSight\Views'):
    for file in files:
        if file.endswith('.cshtml'):
            paths.append(os.path.join(root, file))

for path in paths:
    with open(path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    changed = False
    for i, line in enumerate(lines):
        orig = line
        line = re.sub(r'\(\$\)', '(₱)', line)
        line = re.sub(r"'\$'", "'₱'", line)
        line = re.sub(r'"\$"', '"₱"', line)
        line = re.sub(r'\$(?=\d)', '₱', line)
        line = re.sub(r'\$\$\{', '₱${', line)
        line = re.sub(r'\$@', '₱@', line)
        line = re.sub(r'>\$<', '>₱<', line)
        if line != orig:
            print(f'{os.path.basename(path)}:{i+1}: {orig.strip()}  --->  {line.strip()}')
