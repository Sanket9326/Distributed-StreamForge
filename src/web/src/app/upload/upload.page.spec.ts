import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UploadReceipt } from '../upload.service';
import { UploadPage } from './upload.page';

describe('UploadPage', () => {
  let fixture: ComponentFixture<UploadPage>;
  let http: HttpTestingController;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [UploadPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(UploadPage);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('renders the focused upload workspace', () => {
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Share a video');
    expect(fixture.nativeElement.querySelector('#video-title')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#video-description')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('input[type=file]')).toBeTruthy();
  });

  it('reports progress, renders the receipt, and tracks completion', () => {
    setInputValue('#video-title', 'Demo title');
    setInputValue('#video-description', 'Description');
    selectFile(new File(['video bytes'], 'demo.mp4', { type: 'video/mp4' }));
    findButton('Upload video').click();
    fixture.detectChanges();

    const request = http.expectOne('/api/uploads');
    request.event({ type: HttpEventType.UploadProgress, loaded: 5, total: 10 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('50%');

    const receipt: UploadReceipt = {
      id: 'e2c1bb10-4340-452f-9fc6-a68cf4b12457',
      title: 'Demo title',
      description: 'Description',
      hashtags: [],
      status: 'queued',
      fileName: 'demo.mp4',
      contentType: 'video/mp4',
      sizeBytes: 11,
      uploadedAtUtc: new Date().toISOString(),
      correlationId: 'test-correlation',
    };
    request.flush(receipt, { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Video stored and queued');
    expect(localStorage.getItem('streamforge.pending-uploads.v1')).toContain(receipt.id);
  });

  it('rejects unsupported files before upload', () => {
    selectFile(new File(['text'], 'notes.txt', { type: 'text/plain' }));
    expect(fixture.nativeElement.textContent).toContain('Choose an MP4, MOV, WebM, or MKV video.');
    http.expectNone('/api/uploads');
  });

  function selectFile(file: File): void {
    const input = fixture.nativeElement.querySelector('input[type=file]') as HTMLInputElement;
    Object.defineProperty(input, 'files', {
      configurable: true,
      value: { 0: file, length: 1, item: (index: number) => (index === 0 ? file : null) },
    });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function setInputValue(selector: string, value: string): void {
    const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function findButton(label: string): HTMLButtonElement {
    return (
      Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
    ).find((button) => button.textContent?.includes(label))!;
  }
});
