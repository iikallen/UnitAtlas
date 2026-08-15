import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "UnitAtlas — Traceability",
  description: "Цифровой паспорт каждой произведённой единицы"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="ru">
      <body>{children}</body>
    </html>
  );
}
