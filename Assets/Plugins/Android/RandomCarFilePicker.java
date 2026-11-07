package com.randomcar.content;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.List;

/**
 * Системный выбор файлов Android (Storage Access Framework, ACTION_OPEN_DOCUMENT) для RandomCar: свои треки и картинки наклеек.
 * Игра не просит доступ ко всему хранилищу: пользователь сам выбирает конкретные файлы, и игра сразу копирует их
 * в свою личную папку (временный content:// URI не нужно хранить между запусками).
 * Картинки большого разрешения уменьшаются здесь же (BitmapFactory, inSampleSize) и сохраняются PNG — прозрачность PNG
 * сохраняется, JPEG без прозрачности. Результат приходит в Callback (из фонового потока; C# переносит его в главный поток).
 */
public final class RandomCarFilePicker {
    private static final String TAG = "RandomCar";

    /** Ответ в C# (AndroidJavaProxy). paths и names — через «\n»; error — текст или пустая строка; cancelled — окно закрыто. */
    public interface Callback {
        void onPicked(String paths, String names, String error, boolean cancelled);
    }

    private RandomCarFilePicker() {
    }

    /**
     * Открыть окно выбора. mimeTypes — через запятую («audio/*» или «image/png,image/jpeg»), multiple — несколько файлов,
     * destDir — куда копировать (личная папка игры), maxImageSide — для картинок: больше — уменьшить (0 — не трогать).
     */
    public static void pick(final Activity activity, final String mimeTypes, final boolean multiple, final String destDir,
                            final int maxImageSide, final Callback callback) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    FragmentManager fm = activity.getFragmentManager();
                    PickerFragment fragment = new PickerFragment();
                    fragment.setup(mimeTypes, multiple, destDir, maxImageSide, callback);
                    fm.beginTransaction().add(fragment, "RandomCarFilePicker").commitAllowingStateLoss();
                } catch (Exception e) {
                    Log.e(TAG, "Окно выбора файлов не открылось", e);
                    callback.onPicked("", "", "Окно выбора файлов не открылось: " + e.getMessage(), false);
                }
            }
        });
    }

    /** Невидимый фрагмент: запускает системное окно и получает результат (у UnityPlayerActivity своего обработчика нет). */
    public static final class PickerFragment extends Fragment {
        private static final int REQUEST = 7311;
        private String mimeTypes = "*/*";
        private boolean multiple;
        private String destDir;
        private int maxImageSide;
        private Callback callback;
        private boolean started;

        void setup(String mime, boolean many, String dir, int maxSide, Callback cb) {
            mimeTypes = mime;
            multiple = many;
            destDir = dir;
            maxImageSide = maxSide;
            callback = cb;
        }

        @Override
        public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            if (started || callback == null) return;
            started = true;
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            String[] types = mimeTypes.split(",");
            intent.setType(types.length == 1 ? types[0] : "*/*");
            if (types.length > 1) intent.putExtra(Intent.EXTRA_MIME_TYPES, types);
            intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, multiple);
            try {
                startActivityForResult(intent, REQUEST);
            } catch (Exception e) {
                finish();
                callback.onPicked("", "", "Нет приложения для выбора файлов: " + e.getMessage(), false);
            }
        }

        @Override
        public void onActivityResult(int requestCode, int resultCode, final Intent data) {
            if (requestCode != REQUEST) return;
            final Callback cb = callback;
            final Activity activity = getActivity();
            finish();
            if (cb == null) return;
            if (resultCode != Activity.RESULT_OK || data == null || activity == null) {
                cb.onPicked("", "", "", true);
                return;
            }
            final List<Uri> uris = new ArrayList<>();
            if (data.getClipData() != null) {
                for (int i = 0; i < data.getClipData().getItemCount(); i++) uris.add(data.getClipData().getItemAt(i).getUri());
            } else if (data.getData() != null) {
                uris.add(data.getData());
            }
            final ContentResolver resolver = activity.getContentResolver();
            // Копирование — в фоновом потоке: большие файлы не останавливают игру.
            new Thread(new Runnable() {
                @Override
                public void run() {
                    StringBuilder paths = new StringBuilder();
                    StringBuilder names = new StringBuilder();
                    StringBuilder errors = new StringBuilder();
                    File dir = new File(destDir);
                    if (!dir.exists() && !dir.mkdirs()) errors.append("Нет доступа к папке игры. ");
                    int index = 0;
                    for (Uri uri : uris) {
                        String name = displayName(resolver, uri);
                        try {
                            File target = new File(dir, "picked_" + System.currentTimeMillis() + "_" + (index++) + extension(name));
                            if (maxImageSide > 0) copyImage(resolver, uri, target, maxImageSide);
                            else copy(resolver, uri, target);
                            if (paths.length() > 0) {
                                paths.append('\n');
                                names.append('\n');
                            }
                            paths.append(target.getAbsolutePath());
                            names.append(name);
                        } catch (Exception e) {
                            Log.w(TAG, "Файл не скопирован: " + name, e);
                            errors.append(name).append(": ").append(e.getMessage()).append(". ");
                        }
                    }
                    cb.onPicked(paths.toString(), names.toString(), errors.toString().trim(), false);
                }
            }).start();
        }

        private void finish() {
            try {
                if (getFragmentManager() != null) getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
            } catch (Exception e) {
                Log.w(TAG, "Фрагмент выбора не убран", e);
            }
        }
    }

    static String displayName(ContentResolver resolver, Uri uri) {
        String name = null;
        Cursor cursor = null;
        try {
            cursor = resolver.query(uri, new String[]{OpenableColumns.DISPLAY_NAME}, null, null, null);
            if (cursor != null && cursor.moveToFirst()) name = cursor.getString(0);
        } catch (Exception e) {
            Log.w(TAG, "Имя файла не прочитано", e);
        } finally {
            if (cursor != null) cursor.close();
        }
        if (name == null || name.isEmpty()) name = uri.getLastPathSegment();
        return name == null ? "file" : name;
    }

    static String extension(String name) {
        int dot = name.lastIndexOf('.');
        if (dot < 0 || name.length() - dot > 6) return "";
        String ext = name.substring(dot).toLowerCase();
        for (int i = 1; i < ext.length(); i++) if (!Character.isLetterOrDigit(ext.charAt(i))) return "";
        return ext;
    }

    static void copy(ContentResolver resolver, Uri uri, File target) throws Exception {
        InputStream in = resolver.openInputStream(uri);
        if (in == null) throw new Exception("файл недоступен");
        OutputStream out = new FileOutputStream(target);
        try {
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
        } finally {
            in.close();
            out.close();
        }
    }

    /** Картинка: если больше maxSide — уменьшается при декодировании и сохраняется PNG (прозрачность остаётся), иначе — копия как есть. */
    static void copyImage(ContentResolver resolver, Uri uri, File target, int maxSide) throws Exception {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        InputStream probe = resolver.openInputStream(uri);
        if (probe == null) throw new Exception("файл недоступен");
        try {
            BitmapFactory.decodeStream(probe, null, bounds);
        } finally {
            probe.close();
        }
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) throw new Exception("это не картинка PNG или JPEG");
        if (Math.max(bounds.outWidth, bounds.outHeight) <= maxSide) {
            copy(resolver, uri, target);
            return;
        }
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = 1;
        while (Math.max(bounds.outWidth, bounds.outHeight) / (options.inSampleSize * 2) >= maxSide) options.inSampleSize *= 2;
        InputStream in = resolver.openInputStream(uri);
        if (in == null) throw new Exception("файл недоступен");
        Bitmap bitmap;
        try {
            bitmap = BitmapFactory.decodeStream(in, null, options);
        } finally {
            in.close();
        }
        if (bitmap == null) throw new Exception("картинка не прочитана");
        int w = bitmap.getWidth(), h = bitmap.getHeight();
        float k = Math.min(1f, (float) maxSide / Math.max(w, h));
        if (k < 1f) {
            Bitmap scaled = Bitmap.createScaledBitmap(bitmap, Math.max(1, Math.round(w * k)), Math.max(1, Math.round(h * k)), true);
            if (scaled != bitmap) bitmap.recycle();
            bitmap = scaled;
        }
        File png = new File(target.getParentFile(), target.getName().replaceAll("\\.[^.]*$", "") + ".png");
        OutputStream out = new FileOutputStream(png);
        try {
            bitmap.compress(Bitmap.CompressFormat.PNG, 100, out);
        } finally {
            out.close();
            bitmap.recycle();
        }
        if (!png.getAbsolutePath().equals(target.getAbsolutePath()) && target.exists()) target.delete();
        if (!png.renameTo(target)) copyFile(png, target);
    }

    static void copyFile(File from, File to) throws Exception {
        java.io.FileInputStream in = new java.io.FileInputStream(from);
        OutputStream out = new FileOutputStream(to);
        try {
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
        } finally {
            in.close();
            out.close();
        }
        from.delete();
    }
}
