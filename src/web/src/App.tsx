import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { EntryPage, RefusedPage, ResolvePage, SignInPage, WorkspacePage } from "./pages";

export function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<ResolvePage />} />
        <Route path="/signin" element={<SignInPage />} />
        <Route path="/entry" element={<EntryPage />} />
        <Route path="/workspace" element={<WorkspacePage />} />
        <Route path="/refused" element={<RefusedPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}
