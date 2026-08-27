import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { App } from './app';
import { UploadReceipt } from './upload.service';

describe('App', () => {
  let fixture: ComponentFixture<App>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(App);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('renders the upload workspace', () => {
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Send your source');
    expect(fixture.nativeElement.querySelector('input[type=file]')).toBeTruthy();
  });

  it('rejects unsupported files before upload', () => {
    selectFile(new File(['not a video'], 'notes.txt', { type: 'text/plain' }));

    expect(fixture.nativeElement.textContent).toContain('Choose an MP4, MOV, WebM, or MKV video.');
    http.expectNone('/api/uploads');
  });

  it('reports progress and renders the upload receipt', () => {
    selectFile(new File(['video bytes'], 'demo.mp4', { type: 'video/mp4' }));
    clickButton('Upload video');

    const request = http.expectOne('/api/uploads');
    expect(request.request.method).toBe('POST');
    expect(request.request.reportProgress).toBe(true);
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);

    request.event({ type: HttpEventType.UploadProgress, loaded: 5, total: 10 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('50%');

    const receipt: UploadReceipt = {
      id: 'e2c1bb104340452f9fc6a68cf4b12457',
      fileName: 'demo.mp4',
      contentType: 'video/mp4',
      sizeBytes: 11,
      uploadedAtUtc: '2026-08-27T00:00:00Z',
      correlationId: 'test-correlation',
    };
    request.flush(receipt, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Source stored successfully');
    expect(fixture.nativeElement.textContent).toContain('test-correlation');
  });

  it('cancels an active upload', () => {
    selectFile(new File(['video bytes'], 'demo.webm', { type: 'video/webm' }));
    clickButton('Upload video');

    const request = http.expectOne('/api/uploads');
    clickButton('Cancel upload');

    expect(request.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Upload cancelled');
  });

  function selectFile(file: File): void {
    const input = fixture.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    const files = {
      0: file,
      length: 1,
      item: (index: number) => (index === 0 ? file : null),
    } as unknown as FileList;
    Object.defineProperty(input, 'files', { configurable: true, value: files });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function clickButton(label: string): void {
    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    const button = buttons.find((candidate) => candidate.textContent?.includes(label));
    expect(button).toBeTruthy();
    button!.click();
    fixture.detectChanges();
  }
});
