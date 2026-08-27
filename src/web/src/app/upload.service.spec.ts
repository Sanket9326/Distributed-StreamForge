import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UploadService } from './upload.service';

describe('UploadService', () => {
  let service: UploadService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(UploadService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('adds an inferred video content type for an MKV file', () => {
    service.upload(new File(['content'], 'source.mkv')).subscribe();

    const request = http.expectOne('/api/uploads');
    const submittedFile = (request.request.body as FormData).get('file') as File;
    expect(submittedFile.name).toBe('source.mkv');
    expect(submittedFile.type).toBe('video/x-matroska');
    request.flush({});
  });
});
