# -*- coding: utf-8 -*-
"""
PDF 批量分解脚本（PyMuPDF + MarkItDown 合并版）

把指定文件夹（図面文件夹）中的所有 PDF 一次分解，结果保存到该文件夹下的两个子文件夹：
  1) <文件夹>/xy/    ：每个 PDF 生成 <図番号>_xy.txt    （PyMuPDF 提取的文字及坐标）
  2) <文件夹>/table/ ：每个 PDF 生成 <図番号>_table.txt （MarkItDown 提取的表格）

用法:
    python PDF_SPLIT.py <PDF所在文件夹>
"""

import os
import sys

# 保证被 C# 程序调用（重定向输出）时不乱码
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass


def extract_xy(pdf_path, out_path):
    """用 PyMuPDF 提取每页文字及坐标"""
    import fitz

    doc = fitz.open(pdf_path)
    try:
        with open(out_path, "w", encoding="utf-8") as f:
            for page_num in range(len(doc)):
                page = doc[page_num]
                words = page.get_text("words")
                f.write(f"--- 第 {page_num + 1} 页 (共 {len(words)} 个词) ---\n")
                for w in words:
                    x0, y0, x1, y1 = w[:4]
                    text = w[4]
                    f.write(f"文字: '{text}' | 坐标: x0={x0:.2f}, y0={y0:.2f}, x1={x1:.2f}, y1={y1:.2f}\n")
    finally:
        doc.close()


def extract_table(pdf_path, out_path):
    """用 MarkItDown 提取表格等内容"""
    from markitdown import MarkItDown

    md = MarkItDown()
    result = md.convert(pdf_path)
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(result.text_content)


def find_pdfs(folder):
    """找出文件夹第一层中所有 PDF（扩展名不区分大小写，不进入子文件夹）"""
    return sorted(
        name for name in os.listdir(folder)
        if name.lower().endswith(".pdf") and os.path.isfile(os.path.join(folder, name))
    )


def main():
    if len(sys.argv) < 2:
        print("缺少参数！用法: python PDF_SPLIT.py <PDF所在文件夹>")
        return 1

    folder = sys.argv[1]
    if not os.path.isdir(folder):
        print(f"ERROR: 找不到文件夹: {folder}")
        return 1

    pdfs = find_pdfs(folder)
    if not pdfs:
        print("ERROR: 该文件夹中没有找到 PDF 文件！")
        return 1

    # 两个结果子文件夹（都建在 PDF 所在文件夹里）
    xy_dir = os.path.join(folder, "xy")
    table_dir = os.path.join(folder, "table")
    os.makedirs(xy_dir, exist_ok=True)
    os.makedirs(table_dir, exist_ok=True)

    errors = []
    ok_count = 0
    total = len(pdfs)

    for i, name in enumerate(pdfs, start=1):
        pdf_path = os.path.join(folder, name)
        # 図番号 = PDF 文件名（不含扩展名）
        stem = os.path.splitext(name)[0]
        xy_path = os.path.join(xy_dir, stem + "_xy.txt")
        table_path = os.path.join(table_dir, stem + "_table.txt")

        print(f"[{i}/{total}] 正在处理: {name}")
        failed = False

        # 1) PyMuPDF：文字 + 坐标
        try:
            extract_xy(pdf_path, xy_path)
        except Exception as e:
            errors.append(f"{name}: 坐标提取 (PyMuPDF) 失败: {e}")
            failed = True

        # 2) MarkItDown：表格
        try:
            extract_table(pdf_path, table_path)
        except Exception as e:
            errors.append(f"{name}: 表格提取 (MarkItDown) 失败: {e}")
            failed = True

        if not failed:
            ok_count += 1

    if errors:
        for msg in errors:
            print("ERROR: " + msg)
        print(f"FINISHED {ok_count}/{total}（有失败）")
        return 1

    print(f"SUCCESS {ok_count}/{total}")
    print(f"XY_DIR: {xy_dir}")
    print(f"TABLE_DIR: {table_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
